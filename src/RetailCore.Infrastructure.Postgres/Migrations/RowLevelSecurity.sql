-- Row level security. TEN-02.
--
-- This is the primary tenancy control, not a convenience. EF Core query filters (TEN-03) are
-- defence in depth: they protect queries the application writes through the model, and they
-- protect nothing written by hand, by a migration, by a reporting tool, or by a future
-- developer who reaches for raw SQL under time pressure. These policies hold regardless.
--
-- The current shop comes from the `app.shop_id` setting, which TenantShopInterceptor issues
-- with SET LOCAL at the start of every transaction. SET LOCAL scopes it to the transaction,
-- so a pooled connection cannot carry one shop's identity into the next request.
--
-- current_setting(..., true) returns NULL rather than raising when the setting is absent,
-- and a NULL comparison is never true, so a connection that has not identified a shop sees
-- no rows at all. Failing closed is the only acceptable default here.

CREATE OR REPLACE FUNCTION app_current_shop_id() RETURNS uuid
    LANGUAGE sql STABLE
AS $$
SELECT NULLIF(current_setting('app.shop_id', true), '')::uuid;
$$;


-- The role the application connects as.
--
-- This part is not optional, and it is the part most easily got wrong in deployment.
-- PostgreSQL exempts two kinds of user from row level security: superusers, and roles with
-- BYPASSRLS. A superuser connection ignores every policy below without any error to say so,
-- and the isolation looks like it is working right up until it is tested properly.
--
-- So the application gets its own unprivileged role. Migrations still run as the owner,
-- which is why the policies also say FORCE, but no application traffic ever does.
--
-- Created NOLOGIN here; deployment grants LOGIN and a password separately, so no credential
-- ever appears in this file or in the repository. PLAN.md section 5: secrets are never in the
-- repository.
DO
$$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'retailcore_app') THEN
            CREATE ROLE retailcore_app NOLOGIN NOBYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE;
        END IF;
    END
$$;

GRANT USAGE ON SCHEMA public TO retailcore_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO retailcore_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO retailcore_app;

-- So a later migration's tables are covered without anyone remembering to grant them.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO retailcore_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO retailcore_app;

DO
$$
    DECLARE
        target text;
        -- Every table that carries its own "ShopId".
        --
        -- "Shops" is absent on purpose: it is the tenant list itself, and access to it is
        -- granted by role rather than filtered by shop. "ProductBarcodes" is absent because
        -- it has no ShopId of its own and is handled separately below.
        tenant_tables text[] := ARRAY [
            'Terminals', 'Users', 'Categories', 'Products', 'TaxRates',
            'Sales', 'SaleLines', 'Payments', 'Refunds', 'RefundLines',
            'StockMovements', 'Shifts', 'CashMovements', 'AuditEntries',
            'Outbox', 'ReceiptCounters'
            ];
    BEGIN
        FOREACH target IN ARRAY tenant_tables
            LOOP
                EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY;', target);

                -- FORCE applies the policy to the table owner as well. Without it the
                -- application's own role, if it owns the tables, bypasses every policy here
                -- and the whole exercise is decorative.
                EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY;', target);

                EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I;', target);

                -- USING governs what can be read, updated or deleted.
                -- WITH CHECK governs what can be written, which stops a row being inserted
                -- into another shop as effectively as it stops one being read.
                EXECUTE format(
                        'CREATE POLICY tenant_isolation ON %I
                             USING ("ShopId" = app_current_shop_id())
                             WITH CHECK ("ShopId" = app_current_shop_id());',
                        target);
            END LOOP;
    END
$$;

-- ProductBarcodes has no ShopId of its own; it hangs off a product. It still needs covering,
-- so its policy follows the owning product instead of reading a column.
ALTER TABLE "ProductBarcodes" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "ProductBarcodes" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON "ProductBarcodes";
CREATE POLICY tenant_isolation ON "ProductBarcodes"
    USING (EXISTS (SELECT 1
                   FROM "Products" p
                   WHERE p."Id" = "ProductBarcodes"."ProductId"
                     AND p."ShopId" = app_current_shop_id()))
    WITH CHECK (EXISTS (SELECT 1
                        FROM "Products" p
                        WHERE p."Id" = "ProductBarcodes"."ProductId"
                          AND p."ShopId" = app_current_shop_id()));


-- Immutability of financial records. CMP-04.
--
-- A completed sale, a payment, a refund, a stock movement and an audit entry are all history.
-- Correcting one means writing a compensating record, never editing the original. Enforced
-- with triggers rather than by convention, because the application is not the only thing that
-- will ever hold a connection to this database.

CREATE OR REPLACE FUNCTION app_forbid_change() RETURNS trigger
    LANGUAGE plpgsql
AS
$$
BEGIN
    RAISE EXCEPTION
        'Table % is append only. Record a correcting entry instead of changing %.',
        TG_TABLE_NAME, TG_OP;
END;
$$;

-- Fully append-only tables: nothing about these rows may change once written.
DO
$$
    DECLARE
        target text;
        append_only text[] := ARRAY ['StockMovements', 'AuditEntries', 'Payments', 'CashMovements'];
    BEGIN
        FOREACH target IN ARRAY append_only
            LOOP
                EXECUTE format('DROP TRIGGER IF EXISTS %I ON %I;', target || '_append_only', target);
                EXECUTE format(
                        'CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I
                             FOR EACH ROW EXECUTE FUNCTION app_forbid_change();',
                        target || '_append_only', target);
            END LOOP;
    END
$$;

-- Sales are different: a sale is mutable while it is being rung up, and frozen once it
-- completes. The only permitted change to a completed sale is the void, which sets a status
-- and a reason and touches nothing financial.
CREATE OR REPLACE FUNCTION app_sales_immutable_once_completed() RETURNS trigger
    LANGUAGE plpgsql
AS
$$
BEGIN
    IF OLD."Status" NOT IN ('Completed', 'Voided') THEN
        RETURN NEW;
    END IF;

    IF OLD."Status" = 'Completed' AND NEW."Status" = 'Voided'
        AND NEW."TotalMinor" = OLD."TotalMinor"
        AND NEW."TaxMinor" = OLD."TaxMinor"
        AND NEW."NetMinor" = OLD."NetMinor"
        AND NEW."GrossMinor" = OLD."GrossMinor"
        AND NEW."DiscountMinor" = OLD."DiscountMinor"
        AND NEW."ReceiptNumber" IS NOT DISTINCT FROM OLD."ReceiptNumber" THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION
        'Receipt % is % and cannot be changed. Void it or refund against it.',
        OLD."ReceiptNumber", OLD."Status";
END;
$$;

DROP TRIGGER IF EXISTS sales_immutable ON "Sales";
CREATE TRIGGER sales_immutable
    BEFORE UPDATE
    ON "Sales"
    FOR EACH ROW
EXECUTE FUNCTION app_sales_immutable_once_completed();

DROP TRIGGER IF EXISTS sales_no_delete ON "Sales";
CREATE TRIGGER sales_no_delete
    BEFORE DELETE
    ON "Sales"
    FOR EACH ROW
EXECUTE FUNCTION app_forbid_change();

-- A completed refund is history for the same reasons.
CREATE OR REPLACE FUNCTION app_refunds_immutable_once_completed() RETURNS trigger
    LANGUAGE plpgsql
AS
$$
BEGIN
    IF OLD."CompletedAt" IS NULL THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'Refund % is completed and cannot be changed.', OLD."Id";
END;
$$;

DROP TRIGGER IF EXISTS refunds_immutable ON "Refunds";
CREATE TRIGGER refunds_immutable
    BEFORE UPDATE
    ON "Refunds"
    FOR EACH ROW
EXECUTE FUNCTION app_refunds_immutable_once_completed();

DROP TRIGGER IF EXISTS refunds_no_delete ON "Refunds";
CREATE TRIGGER refunds_no_delete
    BEFORE DELETE
    ON "Refunds"
    FOR EACH ROW
EXECUTE FUNCTION app_forbid_change();
