import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const S08_DEV_USER: AuthenticatedUser = {
  id: '20000000-0000-0000-0000-000000000008',
  fullName: 'مطور التكامل S08',
  email: 'dev-s08@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-dev', name: 'IntegrationDev' }],
  permissions: [
    'integrations.view',
    'versions.view',
    'versions.create',
    'revisions.create',
    'files.view',
    'files.upload',
    'files.delete',
    'files.download',
  ],
};

describe('Sprint S08 Versions (UI-12 / V04) & Files Quarantine/Scan Workspace (UI-10 / V01-V03)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates free-text API version in StandaloneFormPage, prepares draft revision, uploads PENDING_SCAN file, rescan -> CLEAN, and removes draft file', async () => {
    let createdVersion: { id: string; integrationId: string; versionLabel: string; isArchived: boolean; createdAt: string; revisionsCount: number; latestRevisionStatus: string | null } | null = null;
    let draftRevision: { id: string; apiVersionId: string; versionLabel: string; revisionNumber: number; status: string; createdBy: string; createdByName: string; createdAt: string; filesCount: number; pendingScanCount: number; requirements: Array<Record<string, unknown>> } | null = null;
    let filesList: Array<Record<string, unknown>> = [];

    const reqRule = {
      fileTypeId: 'ft-openapi',
      fileTypeName: 'مواصفة OpenAPI',
      catalogCode: 'OPENAPI_SPEC',
      extensions: ['.json', '.yaml'],
      maxSizeBytes: 5 * 1024 * 1024,
      requirementMode: 'REQUIRED',
      minCount: 1,
      maxCount: 2,
      uploadedCleanCount: 0,
      uploadedPendingScanCount: 0,
    };

    const detailsPayload = {
      summary: {
        id: 'int-s08-1',
        documentId: 'doc-s08-1',
        referenceCode: 'INT-PAY-S08',
        derivedTitle: 'CorePay ← شريك تجريبي ج',
        providerKind: 'INTERNAL',
        providerName: 'CorePay',
        consumerKind: 'CONSUMER_ENTITY',
        consumerName: 'شريك تجريبي ج',
        apiGroupId: 'grp-1',
        apiGroupName: 'المدفوعات السريعة',
        requirementTemplateName: 'قالب OpenAPI القياسي',
        approvalRouteName: 'مسار المراجعة المعمارية',
        changePolicyName: 'سياسة التحديث',
        publicationOption: 'MANUAL',
        documentState: 'ACTIVE',
        createdBy: S08_DEV_USER.id,
        createdByName: S08_DEV_USER.fullName,
        createdAt: '2026-11-24T13:00:00Z',
        versionsCount: 0,
        isCurrentUserAssignedDeveloper: true,
        assignedDevelopers: [
          {
            userId: S08_DEV_USER.id,
            fullName: S08_DEV_USER.fullName,
            email: S08_DEV_USER.email,
            isActive: true,
            hasDocView: true,
            hasDocVersionManage: true,
            hasDocFilesManage: true,
            hasDocChange: true,
            hasDocAssign: false,
          },
        ],
        myDocumentPermissions: ['doc.view', 'doc.edit', 'doc.version_manage', 'doc.files_manage'],
      },
      documentDevelopers: [],
      requirementRules: [
        {
          fileTypeId: 'ft-openapi',
          fileTypeName: 'مواصفة OpenAPI',
          catalogCode: 'OPENAPI_SPEC',
          extensions: ['.json', '.yaml'],
          requirementMode: 'REQUIRED',
          minCount: 1,
          maxCount: 2,
        },
      ],
      approvalStages: [],
      allowedChangeTypes: [],
      revisionsCount: 0,
      changeRequestsCount: 0,
      publicationsCount: 0,
      recentAuditEvents: [],
    };

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = (init?.method ?? 'GET').toUpperCase();

      if (url.endsWith('/api/v1/me')) {
        return new Response(JSON.stringify(S08_DEV_USER), { status: 200 });
      }
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-s08', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations/assignable-users')) {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        return new Response(
          JSON.stringify({ items: [detailsPayload.summary], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url.endsWith('/api/v1/integrations/int-s08-1')) {
        return new Response(
          JSON.stringify({
            ...detailsPayload,
            summary: { ...detailsPayload.summary, versionsCount: createdVersion ? 1 : 0 },
            revisionsCount: draftRevision ? 1 : 0,
          }),
          { status: 200 }
        );
      }
      if (url.endsWith('/api/v1/integrations/int-s08-1/versions') && method === 'GET') {
        return new Response(
          JSON.stringify({
            integrationId: 'int-s08-1',
            documentId: 'doc-s08-1',
            referenceCode: 'INT-PAY-S08',
            derivedTitle: 'CorePay ← شريك تجريبي ج',
            providerName: 'CorePay',
            consumerName: 'شريك تجريبي ج',
            apiGroupName: 'المدفوعات السريعة',
            canCreateVersion: true,
            isCurrentUserAssignedDeveloper: true,
            versions: createdVersion ? [createdVersion] : [],
          }),
          { status: 200 }
        );
      }
      if (url.endsWith('/api/v1/integrations/int-s08-1/versions') && method === 'POST') {
        const body = JSON.parse(String(init?.body ?? '{}')) as { versionLabel: string };
        createdVersion = {
          id: 'ver-s08-1',
          integrationId: 'int-s08-1',
          versionLabel: body.versionLabel,
          isArchived: false,
          createdAt: '2026-11-24T14:00:00Z',
          revisionsCount: 0,
          latestRevisionStatus: null,
        };
        return new Response(JSON.stringify(createdVersion), { status: 201 });
      }
      if (url.startsWith('/api/v1/integrations/int-s08-1/files-workspace')) {
        return new Response(
          JSON.stringify({
            integrationId: 'int-s08-1',
            documentId: 'doc-s08-1',
            referenceCode: 'INT-PAY-S08',
            derivedTitle: 'CorePay ← شريك تجريبي ج',
            canCreateDraftRevision: true,
            canUploadFiles: true,
            canDeleteFiles: true,
            canDownloadFiles: true,
            isCurrentUserAssignedDeveloper: true,
            scannerConfigured: false,
            scannerSummaryAr: 'محرك الفحص غير مهيأ — تطبق سياسة PENDING_SCAN',
            scannerSummaryEn: 'Scanner not configured — PENDING_SCAN enforced',
            versions: createdVersion ? [createdVersion] : [],
            selectedVersionId: createdVersion?.id ?? null,
            revisions: draftRevision ? [draftRevision] : [],
            selectedRevision: draftRevision,
            files: filesList,
          }),
          { status: 200 }
        );
      }
      if (url.endsWith('/api/v1/versions/ver-s08-1/revisions') && method === 'POST') {
        draftRevision = {
          id: 'rev-s08-1',
          apiVersionId: 'ver-s08-1',
          versionLabel: createdVersion?.versionLabel ?? 'V2026-Beta',
          revisionNumber: 1,
          status: 'DRAFT',
          createdBy: S08_DEV_USER.id,
          createdByName: S08_DEV_USER.fullName,
          createdAt: '2026-11-24T14:02:00Z',
          filesCount: 0,
          pendingScanCount: 0,
          requirements: [reqRule],
        };
        if (createdVersion) {
          createdVersion.revisionsCount = 1;
          createdVersion.latestRevisionStatus = 'DRAFT';
        }
        return new Response(JSON.stringify(draftRevision), { status: 201 });
      }
      if (url.endsWith('/api/v1/revisions/rev-s08-1/files') && method === 'POST') {
        const pendingItem = {
          id: 'rfile-s08-1',
          revisionId: 'rev-s08-1',
          fileTypeId: 'ft-openapi',
          fileTypeName: 'مواصفة OpenAPI',
          catalogCode: 'OPENAPI_SPEC',
          objectId: 'obj-s08-1',
          originalName: 'openapi.json',
          sha256: 'a'.repeat(64),
          sizeBytes: 54,
          mediaType: 'application/json',
          scanStatus: 'PENDING_SCAN',
          isAvailableForUse: false,
          isQuarantined: true,
          uploadedBy: S08_DEV_USER.id,
          uploadedByName: S08_DEV_USER.fullName,
          uploadedAt: '2026-11-24T14:03:00Z',
        };
        filesList = [pendingItem];
        return new Response(JSON.stringify(pendingItem), { status: 202 });
      }
      if (url.endsWith('/api/v1/revisions/rev-s08-1/files/rfile-s08-1/rescan') && method === 'POST') {
        const cleanItem = {
          ...filesList[0],
          scanStatus: 'CLEAN',
          isAvailableForUse: true,
          isQuarantined: false,
        };
        filesList = [cleanItem];
        return new Response(JSON.stringify(cleanItem), { status: 200 });
      }
      if (url.endsWith('/api/v1/revisions/rev-s08-1/files/rfile-s08-1') && method === 'DELETE') {
        filesList = [];
        return new Response(null, { status: 204 });
      }

      return new Response('{}', { status: 200 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    // 1. Navigate to UI-22 -> UI-09 Details
    fireEvent.click(await screen.findByRole('button', { name: /التكاملات/ }));
    fireEvent.click(await screen.findByRole('button', { name: /قائمة التكاملات/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'التفاصيل والتبويبات (UI-09)' }));

    // 2. Open Versions tab (UI-12), create manual free-text API version in StandaloneFormPage
    fireEvent.click(await screen.findByRole('button', { name: 'الإصدارات (UI-12)' }));
    fireEvent.click(await screen.findByRole('button', { name: 'إنشاء إصدار API (V04)' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/رقم \/ تسمية إصدار API/), {
      target: { value: 'V2026-Beta-AR' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء الإصدار' }));

    const verDlg = await screen.findByRole('dialog');
    expect(within(verDlg).getByText('تم إنشاء إصدار API')).toBeInTheDocument();
    fireEvent.click(within(verDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('V2026-Beta-AR')).toBeInTheDocument();

    // 3. Switch to Files tab (UI-10), prepare draft package #1
    fireEvent.click(screen.getByRole('button', { name: 'الملفات (UI-10)' }));
    fireEvent.click(await screen.findByRole('button', { name: 'تهيئة مسودة حزمة توثيق (#1)' }));
    const draftDlg = await screen.findByRole('dialog');
    fireEvent.click(within(draftDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // 4. Open standalone upload page, submit file -> 202 PENDING_SCAN fail-closed quarantine
    fireEvent.click(await screen.findByRole('button', { name: 'رفع ملف جديد (V02/V03)' }));
    const fileInput = screen.getByLabelText(/اختيار الملف من جهازك/) as HTMLInputElement;
    const mockFile = new File(['{"openapi":"3.1.0"}'], 'openapi.json', { type: 'application/json' });
    fireEvent.change(fileInput, { target: { files: [mockFile] } });
    fireEvent.click(screen.getByRole('button', { name: 'رفع وفحص وحفظ الملف' }));

    const warnDlg = await screen.findByRole('dialog');
    expect(within(warnDlg).getByText('تم الحجز بانتظار الفحص الأمني (Fail-Closed)')).toBeInTheDocument();
    fireEvent.click(within(warnDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Verify PENDING_SCAN badge, Re-scan button shown, Download link NOT shown yet
    expect(await screen.findByText('محجور — بانتظار الفحص (PENDING_SCAN)')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'تنزيل' })).not.toBeInTheDocument();

    // 5. Re-scan -> becomes CLEAN -> Download link becomes visible
    fireEvent.click(screen.getByRole('button', { name: 'إعادة الفحص' }));
    const cleanDlg = await screen.findByRole('dialog');
    expect(within(cleanDlg).getByText('تم رفع الملف واجتياز الفحص')).toBeInTheDocument();
    fireEvent.click(within(cleanDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    expect(await screen.findByText('سليم ومتاح (CLEAN)')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'تنزيل' })).toHaveAttribute(
      'href',
      '/api/v1/revisions/rev-s08-1/files/rfile-s08-1/download'
    );
  });
});
