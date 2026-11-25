import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthenticatedUser } from '../api/authApi';
import { IntegrationsPage } from '../components/integrations/IntegrationsPage';

const S11_DEV_USER: AuthenticatedUser = {
  id: '50000000-0000-0000-0000-000000000011',
  fullName: 'مطور التغيير S11',
  email: 'dev-s11@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-dev-s11', name: 'IntegrationDev' }],
  permissions: [
    'integrations.view',
    'versions.view',
    'revisions.view',
    'files.view',
    'changes.view',
    'changes.create',
    'changes.edit',
    'changes.submit',
  ],
};

describe('Sprint S11 Change Requests (UI-13 / W05-W06) & Internal Notifications (W07)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates file-scoped change request in StandaloneFormPage, submits to independent route (W06), and marks notifications read (W07)', async () => {
    const crList: Array<Record<string, unknown>> = [];
    let unreadCount = 1;
    let notifRead = false;

    const summaryItem = {
      id: 'int-s11-1',
      documentId: 'doc-s11-1',
      referenceCode: 'INT-CR-S11',
      derivedTitle: 'CorePay ← تطبيق S11',
      providerKind: 'INTERNAL',
      providerName: 'CorePay',
      consumerKind: 'CONSUMER_ENTITY',
      consumerName: 'تطبيق S11',
      apiGroupId: 'grp-1',
      apiGroupName: 'المدفوعات',
      requirementTemplateName: 'قالب S11',
      approvalRouteName: 'المسار القياسي',
      changePolicyName: 'سياسة التحديث',
      publicationOption: 'MANUAL',
      documentState: 'ACTIVE',
      createdBy: S11_DEV_USER.id,
      createdByName: S11_DEV_USER.fullName,
      createdAt: '2026-11-25T01:00:00Z',
      versionsCount: 1,
      isCurrentUserAssignedDeveloper: true,
      assignedDevelopers: [],
      myDocumentPermissions: ['doc.view', 'doc.change'],
    };

    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const method = (init?.method ?? 'GET').toUpperCase();

      if (url === '/api/v1/auth/csrf') {
        return new Response(JSON.stringify({ token: 'csrf-s11', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        return new Response(
          JSON.stringify({ items: [summaryItem], totalCount: 1, page: 1, pageSize: 10, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/notifications') {
        return new Response(
          JSON.stringify({
            unreadCount,
            items: [
              {
                id: 'notif-1',
                kind: 'WORKFLOW_APPROVED',
                resourceKind: 'DOCUMENT_REVISION',
                resourceId: 'rev-s11-1',
                summaryAr: 'تم اعتماد مراجعة حزمة التوثيق بالكامل (APPROVED).',
                summaryEn: 'Approved',
                isRead: notifRead,
                createdAt: '2026-11-25T01:05:00Z',
                readAt: notifRead ? '2026-11-25T01:10:00Z' : null,
              },
            ],
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/notifications/notif-1/read' && method === 'POST') {
        unreadCount = 0;
        notifRead = true;
        return new Response(
          JSON.stringify({
            id: 'notif-1',
            kind: 'WORKFLOW_APPROVED',
            resourceKind: 'DOCUMENT_REVISION',
            resourceId: 'rev-s11-1',
            summaryAr: 'تم اعتماد مراجعة حزمة التوثيق بالكامل (APPROVED).',
            summaryEn: 'Approved',
            isRead: true,
            createdAt: '2026-11-25T01:05:00Z',
            readAt: '2026-11-25T01:10:00Z',
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/int-s11-1') {
        return new Response(
          JSON.stringify({
            summary: summaryItem,
            documentDevelopers: [],
            requirementRules: [],
            approvalStages: [],
            allowedChangeTypes: [{ changeTypeId: 'ct-1', changeTypeName: 'تحديث حقل API', routeName: 'لجنة التغيير' }],
            recentAuditEvents: [],
            revisionsCount: 1,
            changeRequestsCount: crList.length,
            publicationsCount: 0,
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/int-s11-1/change-requests' && method === 'GET') {
        return new Response(
          JSON.stringify({
            integrationId: 'int-s11-1',
            documentId: 'doc-s11-1',
            referenceCode: 'INT-CR-S11',
            derivedTitle: 'CorePay ← تطبيق S11',
            canCreate: true,
            canEdit: true,
            canSubmit: true,
            eligibleRevisions: [
              {
                revisionId: 'rev-s11-1',
                apiVersionId: 'ver-s11-1',
                versionLabel: 'V1.0',
                revisionNumber: 1,
                availableFiles: [
                  {
                    revisionFileId: 'rf-spec-1',
                    originalName: 'openapi-payments.json',
                    fileTypeName: 'OpenAPI Spec',
                    sha256: 'a'.repeat(64),
                    sizeBytes: 512,
                  },
                ],
              },
            ],
            allowedChangeTypes: [
              { changeTypeId: 'ct-1', name: 'تحديث حقل API', routeId: 'route-cr-1', routeName: 'لجنة التغيير' },
            ],
            items: crList,
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/int-s11-1/change-requests' && method === 'POST') {
        const created = {
          id: 'cr-1',
          integrationId: 'int-s11-1',
          documentId: 'doc-s11-1',
          referenceCode: 'INT-CR-S11',
          sourceRevisionId: 'rev-s11-1',
          sourceRevisionNumber: 1,
          versionLabel: 'V1.0',
          changeTypeId: 'ct-1',
          changeTypeName: 'تحديث حقل API',
          approvalRouteId: 'route-cr-1',
          approvalRouteName: 'لجنة التغيير',
          reason: 'إضافة حقل التحقق الثنائي',
          consumerImpact: null,
          status: 'DRAFT',
          isEditable: true,
          requestedBy: S11_DEV_USER.id,
          requestedByName: S11_DEV_USER.fullName,
          createdAt: '2026-11-25T01:20:00Z',
          affectedFiles: [
            {
              revisionFileId: 'rf-spec-1',
              originalName: 'openapi-payments.json',
              fileTypeName: 'OpenAPI Spec',
              sha256: 'a'.repeat(64),
              sizeBytes: 512,
            },
          ],
        };
        crList.push(created);
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url === '/api/v1/change-requests/cr-1/submit' && method === 'POST') {
        crList[0] = { ...crList[0], status: 'SUBMITTED', isEditable: false };
        return new Response(
          JSON.stringify({ changeRequest: crList[0], workflowRunId: 'run-cr-1' }),
          { status: 200 }
        );
      }

      return new Response(JSON.stringify({ detail: `Unhandled ${method} ${url}` }), { status: 404 });
    });

    render(
      <IntegrationsPage
        lang="ar"
        permissions={S11_DEV_USER.permissions}
        onSessionExpired={vi.fn()}
      />
    );

    // 1. Open Notifications Center (W07) -> Mark unread notification as read
    fireEvent.click(await screen.findByRole('button', { name: 'الإشعارات الداخلية (W07)' }));
    expect(await screen.findByText('غير مقروء: 1')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'تحديد كمقروء' }));
    expect(await screen.findByText('غير مقروء: 0')).toBeInTheDocument();

    // 2. Navigate to Integration Details -> UI-13 Change Requests tab -> Create & Submit (W05/W06)
    fireEvent.click(screen.getByRole('button', { name: 'العودة إلى قائمة التكاملات' }));
    fireEvent.click(await screen.findByRole('button', { name: 'التفاصيل والتبويبات (UI-09)' }));
    fireEvent.click(await screen.findByRole('button', { name: 'طلبات التغيير (UI-13)' }));

    fireEvent.click(await screen.findByRole('button', { name: 'إنشاء طلب تغيير (W05)' }));
    fireEvent.change(screen.getByLabelText(/سبب طلب التغيير/), { target: { value: 'إضافة حقل التحقق الثنائي' } });
    fireEvent.click(screen.getByLabelText(/openapi-payments.json/));
    fireEvent.click(screen.getByRole('button', { name: 'حفظ كمسودة طلب تغيير' }));

    fireEvent.click(await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('إضافة حقل التحقق الثنائي')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إرسال للاعتماد (W06)' }));
    fireEvent.click(await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('SUBMITTED')).toBeInTheDocument();
  });
});
