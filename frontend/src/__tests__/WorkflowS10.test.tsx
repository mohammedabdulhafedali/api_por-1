import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthenticatedUser } from '../api/authApi';
import { IntegrationsPage } from '../components/integrations/IntegrationsPage';

const S10_REVIEWER_USER: AuthenticatedUser = {
  id: '40000000-0000-0000-0000-000000000010',
  fullName: 'مراجع الأمان S10',
  email: 'reviewer-s10@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-sec', name: 'SecurityReviewer' }],
  permissions: [
    'integrations.view',
    'versions.view',
    'revisions.view',
    'files.view',
    'workflows.view',
    'workflows.decide',
  ],
};

describe('Sprint S10 Sequential Approval Workflow Engine UI (UI-11 / W01-W04)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads Inbox, enforces mandatory notes on RETURN/REJECT in StandaloneFormPage, records APPROVE decision, and updates UI-11 history', async () => {
    const runStatus = 'IN_PROGRESS';
    let stage1Status = 'ACTIVE';
    let stage2Status = 'PENDING';
    let stage1Decision: Record<string, unknown> | null = null;
    let historyEntries: Array<Record<string, unknown>> = [];

    const summaryItem = {
      id: 'int-s10-1',
      documentId: 'doc-s10-1',
      referenceCode: 'INT-WF-S10',
      derivedTitle: 'CorePay ← مصرف S10',
      providerKind: 'INTERNAL',
      providerName: 'CorePay',
      consumerKind: 'CONSUMER_ENTITY',
      consumerName: 'مصرف S10',
      apiGroupId: 'grp-1',
      apiGroupName: 'المدفوعات',
      requirementTemplateName: 'قالب S10',
      approvalRouteName: 'مسار مرحلتين S10',
      changePolicyName: 'سياسة التغيير',
      publicationOption: 'MANUAL',
      documentState: 'ACTIVE',
      createdBy: S10_REVIEWER_USER.id,
      createdByName: S10_REVIEWER_USER.fullName,
      createdAt: '2026-11-25T00:00:00Z',
      versionsCount: 1,
      isCurrentUserAssignedDeveloper: false,
      assignedDevelopers: [],
      myDocumentPermissions: ['doc.view', 'doc.review'],
    };

    const buildRunDetails = () => ({
      id: 'run-s10-1',
      routeId: 'route-1',
      routeName: 'مسار مرحلتين S10',
      revisionId: 'rev-s10-1',
      changeRequestId: null,
      integrationId: 'int-s10-1',
      documentId: 'doc-s10-1',
      referenceCode: 'INT-WF-S10',
      derivedTitle: 'CorePay ← مصرف S10',
      versionLabel: 'V1.0-S10',
      revisionNumber: 1,
      revisionStatus: stage1Status === 'APPROVED' ? 'IN_REVIEW' : 'SUBMITTED',
      status: runStatus,
      submittedBy: S10_REVIEWER_USER.id,
      submittedByName: 'مطور النظام',
      startedAt: '2026-11-25T00:10:00Z',
      endedAt: null,
      activeStageId: stage1Status === 'ACTIVE' ? 'st-1' : 'st-2',
      activeStageName: stage1Status === 'ACTIVE' ? 'المراجعة الأمنية' : 'الاعتماد المعماري',
      activeStageRoleName: stage1Status === 'ACTIVE' ? 'SecurityReviewer' : 'ArchitectReviewer',
      canCurrentUserDecideActiveStage: stage1Status === 'ACTIVE',
      stages: [
        {
          id: 'st-1',
          runId: 'run-s10-1',
          position: 1,
          stageNameSnapshot: 'المراجعة الأمنية',
          responsibleRoleIdSnapshot: 'role-sec',
          responsibleRoleName: 'SecurityReviewer',
          status: stage1Status,
          decision: stage1Decision,
        },
        {
          id: 'st-2',
          runId: 'run-s10-1',
          position: 2,
          stageNameSnapshot: 'الاعتماد المعماري',
          responsibleRoleIdSnapshot: 'role-arch',
          responsibleRoleName: 'ArchitectReviewer',
          status: stage2Status,
          decision: null,
        },
      ],
    });

    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const method = (init?.method ?? 'GET').toUpperCase();

      if (url === '/api/v1/auth/csrf') {
        return new Response(JSON.stringify({ token: 'csrf-s10', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        return new Response(
          JSON.stringify({ items: [summaryItem], totalCount: 1, page: 1, pageSize: 10, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/workflows/inbox') {
        return new Response(
          JSON.stringify({
            items:
              stage1Status === 'ACTIVE'
                ? [
                    {
                      runId: 'run-s10-1',
                      stageId: 'st-1',
                      stagePosition: 1,
                      stageNameSnapshot: 'المراجعة الأمنية',
                      responsibleRoleIdSnapshot: 'role-sec',
                      responsibleRoleName: 'SecurityReviewer',
                      integrationId: 'int-s10-1',
                      documentId: 'doc-s10-1',
                      referenceCode: 'INT-WF-S10',
                      derivedTitle: 'CorePay ← مصرف S10',
                      revisionId: 'rev-s10-1',
                      changeRequestId: null,
                      versionLabel: 'V1.0-S10',
                      revisionNumber: 1,
                      revisionStatus: 'SUBMITTED',
                      submittedBy: S10_REVIEWER_USER.id,
                      submittedByName: 'مطور النظام',
                      startedAt: '2026-11-25T00:10:00Z',
                      canDecide: true,
                    },
                  ]
                : [],
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/int-s10-1') {
        return new Response(
          JSON.stringify({
            summary: summaryItem,
            documentDevelopers: [],
            requirementRules: [],
            approvalStages: [
              { position: 1, stageName: 'المراجعة الأمنية', responsibleRoleName: 'SecurityReviewer' },
              { position: 2, stageName: 'الاعتماد المعماري', responsibleRoleName: 'ArchitectReviewer' },
            ],
            allowedChangeTypes: [],
            recentAudit: [],
            revisionsCount: 1,
            changeRequestsCount: 0,
            publicationsCount: 0,
          }),
          { status: 200 }
        );
      }
      if (url.startsWith('/api/v1/integrations/int-s10-1/files-workspace')) {
        return new Response(
          JSON.stringify({
            integrationId: 'int-s10-1',
            documentId: 'doc-s10-1',
            referenceCode: 'INT-WF-S10',
            derivedTitle: 'CorePay ← مصرف S10',
            canCreateDraftRevision: false,
            canSubmitRevision: false,
            canCreateWorkingCopy: false,
            canUploadFiles: false,
            canDeleteFiles: false,
            canDownloadFiles: true,
            isCurrentUserAssignedDeveloper: false,
            scannerConfigured: true,
            scannerSummaryAr: 'نشط',
            scannerSummaryEn: 'Active',
            versions: [
              {
                id: 'ver-s10-1',
                integrationId: 'int-s10-1',
                versionLabel: 'V1.0-S10',
                isArchived: false,
                createdAt: '2026-11-25T00:00:00Z',
                revisionsCount: 1,
                latestRevisionStatus: stage1Status === 'APPROVED' ? 'IN_REVIEW' : 'SUBMITTED',
              },
            ],
            selectedVersionId: 'ver-s10-1',
            revisions: [
              {
                id: 'rev-s10-1',
                apiVersionId: 'ver-s10-1',
                versionLabel: 'V1.0-S10',
                revisionNumber: 1,
                status: stage1Status === 'APPROVED' ? 'IN_REVIEW' : 'SUBMITTED',
                createdBy: S10_REVIEWER_USER.id,
                createdByName: 'مطور النظام',
                createdAt: '2026-11-25T00:05:00Z',
                filesCount: 1,
                pendingScanCount: 0,
                requirements: [],
                isProtected: true,
                isReadyForSubmission: false,
                latestWorkflowRunId: 'run-s10-1',
                latestWorkflowRunStatus: runStatus,
              },
            ],
            selectedRevision: {
              id: 'rev-s10-1',
              apiVersionId: 'ver-s10-1',
              versionLabel: 'V1.0-S10',
              revisionNumber: 1,
              status: stage1Status === 'APPROVED' ? 'IN_REVIEW' : 'SUBMITTED',
              createdBy: S10_REVIEWER_USER.id,
              createdByName: 'مطور النظام',
              createdAt: '2026-11-25T00:05:00Z',
              filesCount: 1,
              pendingScanCount: 0,
              requirements: [],
              isProtected: true,
              isReadyForSubmission: false,
              latestWorkflowRunId: 'run-s10-1',
              latestWorkflowRunStatus: runStatus,
            },
            files: [],
            approvedChangeRequests: [],
            wordDerivatives: [],
          }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/workflows/run-s10-1') {
        return new Response(JSON.stringify(buildRunDetails()), { status: 200 });
      }
      if (url === '/api/v1/workflows/run-s10-1/history') {
        return new Response(
          JSON.stringify({ runId: 'run-s10-1', revisionId: 'rev-s10-1', entries: historyEntries }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/workflows/run-s10-1/stages/st-1/decisions' && method === 'POST') {
        const body = JSON.parse(String(init?.body ?? '{}')) as { decision: string; notes?: string | null };
        stage1Status = 'APPROVED';
        stage2Status = 'ACTIVE';
        stage1Decision = {
          id: 'dec-1',
          runStageId: 'st-1',
          actorUserId: S10_REVIEWER_USER.id,
          actorUserName: S10_REVIEWER_USER.fullName,
          decision: body.decision,
          notes: body.notes ?? null,
          decidedAt: '2026-11-25T00:20:00Z',
        };
        historyEntries = [
          {
            runId: 'run-s10-1',
            stageId: 'st-1',
            stagePosition: 1,
            stageNameSnapshot: 'المراجعة الأمنية',
            responsibleRoleName: 'SecurityReviewer',
            runStatus: 'IN_PROGRESS',
            revisionId: 'rev-s10-1',
            revisionNumber: 1,
            runStartedAt: '2026-11-25T00:10:00Z',
            decision: stage1Decision,
          },
        ];
        return new Response(
          JSON.stringify({ run: buildRunDetails(), decision: stage1Decision, outcomeStatus: 'IN_PROGRESS' }),
          { status: 200 }
        );
      }

      return new Response(JSON.stringify({ detail: `Unhandled ${method} ${url}` }), { status: 404 });
    });

    render(
      <IntegrationsPage
        lang="ar"
        permissions={S10_REVIEWER_USER.permissions}
        onSessionExpired={vi.fn()}
      />
    );

    // 1. Open Pending Approvals Inbox (W01-W04)
    const inboxBtn = await screen.findByRole('button', { name: 'صندوق المراجعات بانتظار قراري (W01–W04)' });
    fireEvent.click(inboxBtn);
    expect(await screen.findByText('CorePay ← مصرف S10')).toBeInTheDocument();

    // 2. Click Decide Stage -> StandaloneFormPage validates mandatory notes for RETURN
    fireEvent.click(screen.getByRole('button', { name: 'اتخاذ قرار المرحلة (W02)' }));
    const selectChoice = await screen.findByLabelText(/القرار المطلوب/);
    fireEvent.change(selectChoice, { target: { value: 'RETURN' } });
    fireEvent.click(screen.getByRole('button', { name: 'تأكيد وتسجيل القرار' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/الملاحظات إلزامية/);

    // 3. Switch back to APPROVE with notes -> records decision & clears inbox
    fireEvent.change(selectChoice, { target: { value: 'APPROVE' } });
    fireEvent.change(screen.getByLabelText(/ملاحظات المراجع/), { target: { value: 'موافقة المرحلة الأمنية' } });
    fireEvent.click(screen.getByRole('button', { name: 'تأكيد وتسجيل القرار' }));

    const okBtn = await screen.findByRole('button', { name: 'إغلاق نافذة الرسالة' });
    fireEvent.click(okBtn);
    expect(await screen.findByText('لا توجد حاليًا مراحل اعتماد نشطة بانتظار قرار من أدوارك.')).toBeInTheDocument();

    // 4. Open Integration -> UI-11 Revisions tab shows frozen run stages + history entry
    fireEvent.click(screen.getByRole('button', { name: 'العودة إلى قائمة التكاملات' }));
    fireEvent.click(await screen.findByRole('button', { name: 'التفاصيل والتبويبات (UI-09)' }));
    fireEvent.click(await screen.findByRole('button', { name: 'المراجعات (UI-11)' }));

    expect(await screen.findByText('مسار الاعتماد التنفيذي المجمد (W01–W04)')).toBeInTheDocument();
    expect(await screen.findByText('موافقة المرحلة الأمنية')).toBeInTheDocument();
    expect(screen.getByText('المرحلة النشطة حاليًا')).toBeInTheDocument();
  });
});
