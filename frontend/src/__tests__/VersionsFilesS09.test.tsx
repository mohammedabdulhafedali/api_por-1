import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthenticatedUser } from '../api/authApi';
import { DocumentRevisionItem } from '../api/versionsFilesApi';
import { IntegrationsPage } from '../components/integrations/IntegrationsPage';

const S09_DEV_USER: AuthenticatedUser = {
  id: '30000000-0000-0000-0000-000000000009',
  fullName: 'مطور S09',
  email: 'dev-s09@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-dev-s09', name: 'IntegrationDev' }],
  permissions: [
    'integrations.view',
    'versions.view',
    'versions.create',
    'revisions.view',
    'revisions.create',
    'revisions.submit',
    'files.view',
    'files.upload',
    'files.delete',
    'files.download',
  ],
};

describe('Sprint S09 Revisions (UI-11 / V05-V06), Verified Download (V07) & Word OpenXML PoC (V08)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    if (!URL.createObjectURL) {
      Object.defineProperty(URL, 'createObjectURL', {
        writable: true,
        value: vi.fn(() => 'blob:mock-download'),
      });
    } else {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-download');
    }
    if (!URL.revokeObjectURL) {
      Object.defineProperty(URL, 'revokeObjectURL', {
        writable: true,
        value: vi.fn(),
      });
    } else {
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    }
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('supports V05 readiness & submit -> V06 working copy -> V07 verified download -> V08 Word derivative diagnostics & OP-03 gating', async () => {
    let revisionStatus = 'DRAFT';
    let currentRevisions: DocumentRevisionItem[] = [
      {
        id: 'rev-s09-1',
        apiVersionId: 'ver-s09-1',
        versionLabel: 'V2026.9',
        revisionNumber: 1,
        baseRevisionId: null,
        changeRequestId: null,
        status: revisionStatus,
        createdBy: S09_DEV_USER.id,
        createdByName: S09_DEV_USER.fullName,
        createdAt: '2026-11-24T20:00:00Z',
        approvedAt: null,
        filesCount: 2,
        pendingScanCount: 0,
        isProtected: false,
        isReadyForSubmission: true,
        missingRequiredTypesCount: 0,
        submissionBlockersAr: [],
        submissionBlockersEn: [],
        requirements: [
          {
            fileTypeId: 'ft-openapi',
            fileTypeName: 'مواصفة OpenAPI',
            catalogCode: 'OPENAPI_SPEC',
            extensions: ['.json'],
            maxSizeBytes: 5 * 1024 * 1024,
            requirementMode: 'REQUIRED',
            minCount: 1,
            maxCount: 2,
            uploadedCleanCount: 1,
            uploadedPendingScanCount: 0,
            isSatisfied: true,
            missingRequiredCount: 0,
            exceedsMaxCount: false,
          },
        ],
      },
    ];

    const files = [
      {
        id: 'rfile-spec',
        revisionId: 'rev-s09-1',
        fileTypeId: 'ft-openapi',
        fileTypeName: 'مواصفة OpenAPI',
        catalogCode: 'OPENAPI_SPEC',
        objectId: 'obj-spec',
        originalName: 'openapi.json',
        sha256: 'a'.repeat(64),
        sizeBytes: 120,
        mediaType: 'application/json',
        scanStatus: 'CLEAN',
        isAvailableForUse: true,
        isQuarantined: false,
        uploadedBy: S09_DEV_USER.id,
        uploadedByName: S09_DEV_USER.fullName,
        uploadedAt: '2026-11-24T20:01:00Z',
      },
      {
        id: 'rfile-word',
        revisionId: 'rev-s09-1',
        fileTypeId: 'ft-word',
        fileTypeName: 'وثيقة Word',
        catalogCode: 'WORD_DOC',
        objectId: 'obj-word',
        originalName: 'integration-guide.docx',
        sha256: 'b'.repeat(64),
        sizeBytes: 8400,
        mediaType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        scanStatus: 'CLEAN',
        isAvailableForUse: true,
        isQuarantined: false,
        uploadedBy: S09_DEV_USER.id,
        uploadedByName: S09_DEV_USER.fullName,
        uploadedAt: '2026-11-24T20:02:00Z',
      },
    ];

    let wordDerivatives: Array<Record<string, unknown>> = [];
    let selectedRevisionId = 'rev-s09-1';

    const summaryItem = {
      id: 'int-s09-1',
      documentId: 'doc-s09-1',
      referenceCode: 'INT-S09-UI',
      derivedTitle: 'CorePay ← مصرف تجريبي',
      providerKind: 'INTERNAL',
      providerName: 'CorePay',
      consumerKind: 'CONSUMER_ENTITY',
      consumerName: 'مصرف تجريبي',
      apiGroupId: 'grp-1',
      apiGroupName: 'المدفوعات',
      requirementTemplateName: 'قالب S09',
      approvalRouteName: 'مسار المراجعة',
      changePolicyName: 'سياسة التغيير',
      publicationOption: 'MANUAL',
      documentState: 'ACTIVE',
      createdBy: S09_DEV_USER.id,
      createdByName: S09_DEV_USER.fullName,
      createdAt: '2026-11-24T19:00:00Z',
      versionsCount: 1,
      isCurrentUserAssignedDeveloper: true,
      assignedDevelopers: [],
      myDocumentPermissions: ['doc.view', 'doc.edit', 'doc.version_manage', 'doc.files_manage', 'doc.change'],
    };

    const fetchSpy = vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const method = (init?.method ?? 'GET').toUpperCase();

      if (url === '/api/v1/auth/csrf') {
        return new Response(JSON.stringify({ token: 'csrf-s09', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        return new Response(
          JSON.stringify({ items: [summaryItem], totalCount: 1, page: 1, pageSize: 10, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url.startsWith('/api/v1/integrations/eligible-documents') || url.startsWith('/api/v1/integrations/assignable-users')) {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      if (url === '/api/v1/integrations/int-s09-1') {
        return new Response(
          JSON.stringify({
            summary: summaryItem,
            documentDevelopers: [],
            requirementRules: [
              {
                fileTypeId: 'ft-openapi',
                fileTypeName: 'مواصفة OpenAPI',
                catalogCode: 'OPENAPI_SPEC',
                extensions: ['.json'],
                maxSizeBytes: 5 * 1024 * 1024,
                requirementMode: 'REQUIRED',
                minCount: 1,
                maxCount: 2,
              },
            ],
            approvalStages: [{ position: 1, stageName: 'الأمن السيبراني', responsibleRoleName: 'SecurityReviewer' }],
            allowedChangeTypes: [{ id: 'ct-1', name: 'تحديث مواصفة', approvalRouteName: 'مسار المراجعة' }],
            recentAudit: [],
            revisionsCount: currentRevisions.length,
            changeRequestsCount: 1,
            publicationsCount: 0,
          }),
          { status: 200 }
        );
      }
      if (url.startsWith('/api/v1/integrations/int-s09-1/files-workspace')) {
        const selectedRev = currentRevisions.find((r) => r.id === selectedRevisionId) ?? currentRevisions[0];
        return new Response(
          JSON.stringify({
            integrationId: 'int-s09-1',
            documentId: 'doc-s09-1',
            referenceCode: 'INT-S09-UI',
            derivedTitle: 'CorePay ← مصرف تجريبي',
            canCreateDraftRevision: false,
            canSubmitRevision: true,
            canCreateWorkingCopy: true,
            canUploadFiles: true,
            canDeleteFiles: true,
            canDownloadFiles: true,
            isCurrentUserAssignedDeveloper: true,
            scannerConfigured: true,
            scannerSummaryAr: 'محرك الفحص نشط',
            scannerSummaryEn: 'Scanner active',
            versions: [
              {
                id: 'ver-s09-1',
                integrationId: 'int-s09-1',
                versionLabel: 'V2026.9',
                isArchived: false,
                createdAt: '2026-11-24T19:30:00Z',
                revisionsCount: currentRevisions.length,
                latestRevisionStatus: currentRevisions[0].status,
              },
            ],
            selectedVersionId: 'ver-s09-1',
            revisions: currentRevisions,
            selectedRevision: selectedRev,
            files,
            approvedChangeRequests:
              revisionStatus === 'APPROVED'
                ? [
                    {
                      id: 'cr-s09-1',
                      sourceRevisionId: 'rev-s09-1',
                      changeTypeId: 'ct-1',
                      changeTypeName: 'تحديث مواصفة',
                      reason: 'إضافة حقل التحقق المزدوج في المسار',
                      affectedSourceFileIds: ['rfile-spec'],
                      createdAt: '2026-11-24T20:10:00Z',
                    },
                  ]
                : [],
            wordDerivatives,
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/revisions/rev-s09-1/files/rfile-spec/download') {
        return new Response(new Blob(['{"openapi":"3.1.0"}'], { type: 'application/json' }), {
          status: 200,
          headers: {
            'Content-Disposition': 'attachment; filename="openapi.json"',
            'X-File-Sha256': 'a'.repeat(64),
          },
        });
      }
      if (url === '/api/v1/revisions/rev-s09-1/files/rfile-word/word-derivative' && method === 'POST') {
        const record = {
          id: 'wd-1',
          revisionId: 'rev-s09-1',
          revisionFileId: 'rfile-word',
          sourceObjectId: 'obj-word',
          sourceSha256: 'b'.repeat(64),
          derivedObjectId: 'obj-word-derived',
          derivedSha256: 'c'.repeat(64),
          derivedSizeBytes: 8620,
          status: 'VERIFIED',
          templateKindDetected: 'SDT_CONTROLS',
          fieldsUpdatedCount: 2,
          matchedFields: [
            { fieldKey: 'APIGOV_INTEGRATION_CODE', mechanism: 'SDT_CONTENT_CONTROL', valueApplied: 'INT-S09-UI', location: 'MainDocumentBody' },
            { fieldKey: 'APIGOV_VERSION_LABEL', mechanism: 'SDT_CONTENT_CONTROL', valueApplied: 'V2026.9', location: 'MainDocumentBody' },
          ],
          validationErrors: [],
          errorMessage: null,
          isPublishable: true,
          createdBy: S09_DEV_USER.id,
          createdByName: S09_DEV_USER.fullName,
          createdAt: '2026-11-24T20:05:00Z',
        };
        wordDerivatives = [record];
        return new Response(JSON.stringify(record), { status: 200 });
      }
      if (url === '/api/v1/revisions/rev-s09-1/submit' && method === 'POST') {
        revisionStatus = 'SUBMITTED';
        currentRevisions[0] = {
          ...currentRevisions[0],
          status: 'SUBMITTED',
          isProtected: true,
        };
        return new Response(
          JSON.stringify({ revision: currentRevisions[0], cleanFilesCount: 2 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/versions/ver-s09-1/revisions' && method === 'POST') {
        const body = JSON.parse(String(init?.body ?? '{}')) as { baseRevisionId?: string; changeRequestId?: string };
        const rev2 = {
          ...currentRevisions[0],
          id: 'rev-s09-2',
          revisionNumber: 2,
          baseRevisionId: body.baseRevisionId ?? 'rev-s09-1',
          changeRequestId: body.changeRequestId ?? 'cr-s09-1',
          status: 'DRAFT',
          isProtected: false,
        };
        currentRevisions = [rev2, currentRevisions[0]];
        selectedRevisionId = 'rev-s09-2';
        return new Response(JSON.stringify(rev2), { status: 201 });
      }

      return new Response(JSON.stringify({ detail: `Unhandled ${method} ${url}` }), { status: 404 });
    });

    render(
      <IntegrationsPage
        lang="ar"
        permissions={S09_DEV_USER.permissions}
        onSessionExpired={vi.fn()}
      />
    );

    const detailsBtn = await screen.findByRole('button', { name: 'التفاصيل' });
    fireEvent.click(detailsBtn);

    const filesTabBtn = await screen.findByRole('button', { name: 'الملفات' });
    fireEvent.click(filesTabBtn);

    // Checklist shows 1/1-2 (مكتمل) & V07 verified download click
    expect(await screen.findByText(/تقدم استيفاء الملفات الإلزامية قبل التقديم/i)).toBeInTheDocument();
    const dlLinks = await screen.findAllByRole('link', { name: 'تنزيل' });
    expect(dlLinks[0]).toHaveAttribute('href', '/api/v1/revisions/rev-s09-1/files/rfile-spec/download');
    fireEvent.click(dlLinks[0]);
    expect(await screen.findByText(/تم التحقق من الصلاحية وبصمة SHA-256 وتدقيق تنزيل/)).toBeInTheDocument();

    // V08 Word PoC click on clean .docx -> VERIFIED + downloadable derivative + untouched original SHA
    const wordBtn = await screen.findByRole('button', { name: 'تحديث حقول Word' });
    fireEvent.click(wordBtn);
    const confirmWordModalBtn = await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' });
    fireEvent.click(confirmWordModalBtn);

    expect(await screen.findByText('مشتق متحقق وقابل للتنزيل (VERIFIED)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تنزيل المشتق المتحقق' })).toBeInTheDocument();

    // V05 Submit Revision
    const submitBtn = screen.getByRole('button', { name: 'تقديم المراجعة للاعتماد' });
    fireEvent.click(submitBtn);
    const confirmSubmitBtn = await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' });
    fireEvent.click(confirmSubmitBtn);

    // After submit, close success dialog -> protected revision banner appears (V06)
    const okSubmittedBtn = await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' });
    fireEvent.click(okSubmittedBtn);
    expect(await screen.findByText(/ومحمية بالكامل من رفع أو حذف أو تعديل الملفات/)).toBeInTheDocument();

    // Simulate backend workflow approving revision #1, switch to Revisions tab (UI-11), and open Working Copy (V06)
    revisionStatus = 'APPROVED';
    currentRevisions[0] = { ...currentRevisions[0], status: 'APPROVED', isProtected: true };
    fireEvent.click(screen.getByRole('button', { name: 'المراجعات' }));

    const createWcBtns = await screen.findAllByRole('button', { name: 'إنشاء نسخة عمل جديدة' });
    fireEvent.click(createWcBtns[0]);

    expect(await screen.findByText(/إنشاء نسخة عمل جديدة من مراجعة معتمدة/)).toBeInTheDocument();
    const openWcBtn = screen.getByRole('button', { name: 'فتح نسخة العمل' });
    fireEvent.click(openWcBtn);

    const okWcBtn = await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' });
    fireEvent.click(okWcBtn);

    expect(await screen.findByText('مسودة مراجعة #2 · DRAFT')).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/v1/versions/ver-s09-1/revisions',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
