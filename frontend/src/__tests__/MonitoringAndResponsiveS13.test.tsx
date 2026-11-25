import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const FULL_MONITORING_USER: AuthenticatedUser = {
  id: '20000000-0000-0000-0000-000000000013',
  fullName: 'S13 Governance Auditor',
  email: 'auditor@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'Governance Admin' }],
  permissions: [
    'dashboard.view',
    'search.view',
    'audit.view',
    'api_documents.view',
    'integrations.view',
    'workflows.view',
    'publications.view',
    'portal.view',
  ],
};

describe('Sprint S13 • Task P05 (Dashboard, Global Search, Audit Log, Notifications) & U04 Responsive/Theme', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it('renders Permission-Adaptive Dashboard (UI-17), Global Search (UI-18), and Append-Only Audit Log (UI-16)', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.endsWith('/api/v1/dashboard')) {
        return new Response(
          JSON.stringify({
            visibleDocumentsCount: 4,
            visibleIntegrationsCount: 3,
            pendingMyApprovalStagesCount: 1,
            openChangeRequestsCount: 2,
            publishedRevisionsCount: 2,
            unreadNotificationsCount: 5,
            canViewDocuments: true,
            canViewIntegrations: true,
            canReviewWorkflows: true,
            canViewPublications: true,
            canViewAudit: true,
            canViewPortal: true,
            pendingApprovalStages: [
              {
                runId: 'run-1',
                stageId: 'stage-1',
                stageName: 'Security Review',
                targetKind: 'REVISION',
                targetId: 'rev-1',
                documentId: 'doc-1',
                integrationId: 'int-1',
                title: 'Demo Core System ← Demo Partner ERP',
                referenceCode: 'INT-2026-S13',
                roleName: 'Security Officer',
                startedAt: '2026-11-25T08:00:00Z',
              },
            ],
            recentAuditEvents: [
              {
                id: 'aud-1',
                actorName: 'S13 Governance Auditor',
                operation: 'REVISION_PUBLISHED',
                resourceKind: 'PUBLICATION',
                resourceId: 'pub-1',
                result: 'SUCCESS',
                recordedAt: '2026-11-25T08:05:00Z',
              },
            ],
            traceId: 'trace-dash',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.includes('/api/v1/search')) {
        return new Response(
          JSON.stringify({
            query: 'INT-2026-S13',
            totalCount: 1,
            items: [
              {
                resultKind: 'INTEGRATION',
                id: 'int-1',
                documentId: 'doc-1',
                integrationId: 'int-1',
                referenceCode: 'INT-2026-S13',
                title: 'Demo Core System ← Demo Partner ERP',
                providerName: 'Demo Core System',
                consumerName: 'Demo Partner ERP',
                apiGroupName: 'Payments API',
                state: 'ACTIVE',
                latestVersionLabel: '1.0.0',
                hasActivePublication: true,
                createdAt: '2026-11-25T07:00:00Z',
              },
            ],
            traceId: 'trace-search',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.includes('/api/v1/audit-events')) {
        return new Response(
          JSON.stringify({
            totalCount: 1,
            page: 1,
            pageSize: 20,
            items: [
              {
                id: 'aud-99',
                actorUserId: 'u-1',
                actorName: 'S13 Governance Auditor',
                actorEmail: 'auditor@api-governance.local',
                operation: 'PORTAL_FILE_DOWNLOADED',
                resourceKind: 'REVISION_FILE',
                resourceId: 'rf-1',
                result: 'SUCCESS',
                changeSummaryJson: '{"sha256":"abc123"}',
                recordedAt: '2026-11-25T08:10:00Z',
              },
            ],
            distinctOperations: ['PORTAL_FILE_DOWNLOADED'],
            distinctResourceKinds: ['REVISION_FILE'],
            traceId: 'trace-audit',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    globalThis.fetch = fetchMock as unknown as typeof fetch;

    render(<App initialAuthenticatedUser={FULL_MONITORING_USER} />);

    await waitFor(() => {
      expect(screen.getByTestId('dashboard-page')).toBeInTheDocument();
      expect(screen.getByText('INT-2026-S13')).toBeInTheDocument();
      expect(screen.getByText('REVISION_PUBLISHED')).toBeInTheDocument();
    });

    // Open UI-18 Global Search via sidebar button
    fireEvent.click(screen.getByRole('button', { name: /البحث والاستعراض/i }));
    await waitFor(() => {
      expect(screen.getByTestId('search-browse-page')).toBeInTheDocument();
      expect(screen.getByText('Payments API')).toBeInTheDocument();
    });

    // Open UI-16 Audit Log via collapsible Administration group
    fireEvent.click(screen.getByRole('button', { name: 'الإدارة' }));
    fireEvent.click(screen.getByRole('button', { name: /سجل التدقيق/i }));
    await waitFor(() => {
      expect(screen.getByTestId('audit-log-page')).toBeInTheDocument();
      expect(screen.getAllByText('PORTAL_FILE_DOWNLOADED').length).toBeGreaterThanOrEqual(2);
    });
  });

  it('validates U04 responsive mobile drawer toggle, light/dark theme switching, and notification bell control', async () => {
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/v1/search')) {
        return new Response(
          JSON.stringify({ query: '', totalCount: 0, items: [], traceId: 'trace-u04-search' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(
        JSON.stringify({
          visibleDocumentsCount: 0,
          visibleIntegrationsCount: 0,
          pendingMyApprovalStagesCount: 0,
          openChangeRequestsCount: 0,
          publishedRevisionsCount: 0,
          unreadNotificationsCount: 0,
          canViewDocuments: false,
          canViewIntegrations: false,
          canReviewWorkflows: false,
          canViewPublications: false,
          canViewAudit: false,
          canViewPortal: false,
          pendingApprovalStages: [],
          recentAuditEvents: [],
          traceId: 'trace-u04',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } }
      );
    }) as unknown as typeof fetch;

    render(<App initialAuthenticatedUser={FULL_MONITORING_USER} />);

    await waitFor(() => {
      expect(screen.getByTestId('dashboard-page')).toBeInTheDocument();
    });
    expect(screen.getByTestId('topbar-notifications-bell')).toBeInTheDocument();

    const themeToggle = screen.getByRole('button', { name: /تبديل المظهر|Toggle Light\/Dark Theme/i });
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    fireEvent.click(themeToggle);
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');

    const enBtn = screen.getByRole('button', { name: 'EN' });
    fireEvent.click(enBtn);
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');

    fireEvent.click(screen.getByRole('button', { name: /Search & Browse/i }));
    await waitFor(() => {
      expect(screen.getByTestId('search-browse-page')).toBeInTheDocument();
    });
    const searchBox = screen.getByPlaceholderText(/Search provider, consumer/i) as HTMLInputElement;
    fireEvent.change(searchBox, { target: { value: 'KEEP_INPUT_ACROSS_DRAWER' } });

    const mobileBtn = screen.getByRole('button', { name: /Toggle sidebar menu/i });
    fireEvent.click(mobileBtn);
    expect(searchBox.value).toBe('KEEP_INPUT_ACROSS_DRAWER');
  });
});
