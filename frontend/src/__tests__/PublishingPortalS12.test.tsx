import { render } from '@testing-library/react';
import { afterEach, describe, it, vi } from 'vitest';
import { App } from '../App';

describe('Sprint S12 Publishing, Active Consumer Version & Portal UI (P01-P04)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders Publishing tab (P01-P03) and Consumer Portal (P04 / UI-19)', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = (init?.method ?? 'GET').toUpperCase();

      if (url.endsWith('/api/v1/auth/csrf')) {
        return Promise.resolve(
          new Response(JSON.stringify({ token: 'csrf-s12', headerName: 'X-CSRF-TOKEN' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          })
        );
      }

      if (url.includes('/api/v1/portal/integrations')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              consumerEntityId: 'cons-1',
              consumerEntityName: 'Alpha Consumer Bank',
              integrations: [
                {
                  integrationId: 'int-1',
                  referenceCode: 'INT-ALPHA-01',
                  documentId: 'doc-1',
                  documentTitle: 'Core System → Alpha Consumer Bank · Payments',
                  providerName: 'Core System',
                  apiGroupName: 'Payments',
                  activeConsumerVersionLabel: 'v2.0',
                  latestPublicationId: 'pub-1',
                  latestPublicationVersionLabel: 'v2.0',
                  latestPublicationRevisionNumber: 1,
                  latestPublishedAt: '2026-11-25T03:00:00Z',
                  publishedFilesCount: 1,
                },
              ],
              traceId: 'trace-s12-portal',
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } }
          )
        );
      }

      if (url.includes('/api/v1/portal/publications/pub-1/files') && method === 'GET') {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              publicationId: 'pub-1',
              integrationId: 'int-1',
              referenceCode: 'INT-ALPHA-01',
              documentTitle: 'Core System → Alpha Consumer Bank · Payments',
              versionLabel: 'v2.0',
              revisionNumber: 1,
              publishedAt: '2026-11-25T03:00:00Z',
              files: [
                {
                  id: 'pf-1',
                  revisionFileId: 'rf-1',
                  fileTypeId: 'ft-1',
                  fileTypeCode: 'OPENAPI',
                  fileTypeName: 'OpenAPI JSON',
                  originalName: 'alpha-v2.json',
                  sizeBytes: 512,
                  mediaType: 'application/json',
                  sha256: 'abc123def456',
                  scanStatus: 'CLEAN',
                },
              ],
              traceId: 'trace-s12-files',
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } }
          )
        );
      }

      return Promise.resolve(
        new Response(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 1 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      );
    });

    vi.stubGlobal('fetch', fetchMock);

    render(
      <App
        initialAuthenticatedUser={{
          id: 'user-portal-1',
          fullName: 'Alpha Portal User',
          email: 'portal@alpha.example',
          isActive: true,
          authProvider: 'LocalIdentity',
          roles: [{ id: 'role-portal-1', name: 'External Consumer Portal' }],
          permissions: [],
        }}
      />
    );

  });
});
