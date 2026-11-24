-- Clean, non-sensitive synthetic seed data for local development on a new workstation.
-- Safe for GitHub: contains ZERO user accounts, ZERO password hashes, ZERO sessions,
-- ZERO audit logs, ZERO uploaded documents, and ZERO real institutional names.

BEGIN;

-- 1. Synthetic Company Systems (M01)
INSERT INTO company_systems (id, name, owner_email, description, is_active, created_at)
VALUES
  ('11111111-1111-1111-1111-111111111101', 'BillingCore Demo', 'billing-demo@example.invalid', 'Synthetic internal billing platform for local development', true, NOW()),
  ('11111111-1111-1111-1111-111111111102', 'PortalCore Demo', 'portal-demo@example.invalid', 'Synthetic internal portal platform for local development', true, NOW())
ON CONFLICT (id) DO NOTHING;

-- 2. Synthetic Internal Provider (M02)
INSERT INTO internal_provider_systems (id, company_system_id, description, is_active)
VALUES
  ('22222222-2222-2222-2222-222222222201', '11111111-1111-1111-1111-111111111101', 'Synthetic internal API provider', true)
ON CONFLICT (id) DO NOTHING;

-- 3. Synthetic External Provider (M02)
INSERT INTO external_providers (id, name, description, is_active)
VALUES
  ('33333333-3333-3333-3333-333333333301', 'External Partner Demo', 'Synthetic external API provider', true)
ON CONFLICT (id) DO NOTHING;

-- 4. Synthetic Consumer Category & Entities (M03)
INSERT INTO consumer_categories (id, name, description, is_active)
VALUES
  ('44444444-4444-4444-4444-444444444401', 'الشركاء التجريبيون', 'تصنيف افتراضي لاختبار بيئة التطوير', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO consumer_entities (id, category_id, name, description, is_active)
VALUES
  ('55555555-5555-5555-5555-555555555501', '44444444-4444-4444-4444-444444444401', 'جهة مستهلكة تجريبية أ', 'جهة اختبار اصطناعية', true)
ON CONFLICT (id) DO NOTHING;

-- 5. Synthetic API Groups (M04)
INSERT INTO api_groups (id, name, description, is_active)
VALUES
  ('66666666-6666-6666-6666-666666666601', 'مجموعة المدفوعات التجريبية', 'مجموعة API تجريبية لتطوير المنصة', true)
ON CONFLICT (id) DO NOTHING;

-- 6. Approved Common File Types & Extensions (M04)
INSERT INTO file_types (id, catalog_code, display_name, max_size_bytes, word_template_object_id, is_active)
VALUES
  ('77777777-7777-7777-7777-777777777701', 'OPENAPI_SPEC', 'مواصفة OpenAPI / Swagger', 10485760, NULL, true),
  ('77777777-7777-7777-7777-777777777702', 'WORD_DOC', 'مستند Word التوثيقي', 20971520, NULL, true),
  ('77777777-7777-7777-7777-777777777703', 'PDF_DOC', 'مستند PDF', 26214400, NULL, true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO file_type_extensions (id, file_type_id, extension)
VALUES
  ('77777777-7777-7777-7777-777777778801', '77777777-7777-7777-7777-777777777701', '.json'),
  ('77777777-7777-7777-7777-777777778802', '77777777-7777-7777-7777-777777777701', '.yaml'),
  ('77777777-7777-7777-7777-777777778803', '77777777-7777-7777-7777-777777777701', '.yml'),
  ('77777777-7777-7777-7777-777777778804', '77777777-7777-7777-7777-777777777702', '.docx'),
  ('77777777-7777-7777-7777-777777778805', '77777777-7777-7777-7777-777777777703', '.pdf')
ON CONFLICT (id) DO NOTHING;

-- 7. Synthetic File Requirement Template (M05)
INSERT INTO file_requirement_templates (id, name, is_active)
VALUES
  ('88888888-8888-8888-8888-888888888801', 'قالب توثيق API التجريبي', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO file_requirement_rules (id, template_id, file_type_id, requirement_mode, min_count, max_count)
VALUES
  ('88888888-8888-8888-8888-888888889901', '88888888-8888-8888-8888-888888888801', '77777777-7777-7777-7777-777777777701', 'REQUIRED', 1, 2),
  ('88888888-8888-8888-8888-888888889902', '88888888-8888-8888-8888-888888888801', '77777777-7777-7777-7777-777777777702', 'OPTIONAL', 0, 1)
ON CONFLICT (id) DO NOTHING;

-- 8. Standard Non-Sensitive Roles & Clean Approval Route / Change Types / Change Policy (I03/M06/M07)
INSERT INTO roles (id, name, is_pinned, is_active, created_at)
VALUES
  ('99999999-9999-9999-9999-999999999901', 'ReviewerDemoRole', false, true, NOW())
ON CONFLICT (id) DO NOTHING;

INSERT INTO approval_routes (id, name, is_active)
VALUES
  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'مسار المراجعة التجريبي', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO approval_route_stages (id, route_id, position, stage_name, responsible_role_id)
VALUES
  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaab1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 1, 'المراجعة الفنية التجريبية', '99999999-9999-9999-9999-999999999901')
ON CONFLICT (id) DO NOTHING;

INSERT INTO change_types (id, name, route_id, is_active)
VALUES
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1', 'تحديث توثيق تجريبي', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO change_policies (id, name, is_active)
VALUES
  ('cccccccc-cccc-cccc-cccc-ccccccccccc1', 'سياسة التغيير التجريبية', true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO change_policy_types (id, policy_id, change_type_id)
VALUES
  ('cccccccc-cccc-cccc-cccc-ccccccccccd1', 'cccccccc-cccc-cccc-cccc-ccccccccccc1', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1')
ON CONFLICT (id) DO NOTHING;

COMMIT;

