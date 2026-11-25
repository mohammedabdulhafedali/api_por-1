import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  getPortalIntegrations,
  getPortalPublicationFileDownloadUrl,
  getPortalPublicationFiles,
  PortalIntegrationItem,
  PortalIntegrationsResponse,
  PortalPublicationFilesResponse,
} from '../../api/publishingPortalApi';
import { downloadBinaryFileVerified } from '../../api/versionsFilesApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  ErrorState,
  LoadingState,
  StatusBadge,
} from '../ui';

interface ConsumerPortalPageProps {
  lang: AppLanguage;
  onSessionExpired: () => void;
}

const PORTAL_TEXT = {
  ar: {
    pageBadge: 'بوابة المستهلك الخارجي · UI-19 / P04',
    title: 'بوابة المستهلك الخارجي وتنزيل الحزم المنشورة',
    subtitle:
      'تعرض هذه البوابة فقط التكاملات المنشورة والمعتمدة والمربوطة بجهة المستهلك الخاصة بحسابك، مع التحقق من البصمة الأمنية SHA-256 وتسجيل كل عملية تنزيل.',
    consumerScopeLabel: 'الجهة المستهلكة المصرح لها',
    emptyIntegrations: 'لا توجد حزم منشورة نشطة مرتبطة بجهة المستهلك الخاصة بحسابك حاليًا.',
    refreshBtn: 'تحديث البوابة',
    colRef: 'رمز التكامل',
    colDoc: 'الوثيقة / المزود',
    colActiveVer: 'الإصدار المعتمد حاليًا',
    colLatestPub: 'الحزمة المنشورة',
    colFilesCount: 'عدد الملفات',
    colActions: 'الإجراءات',
    openFilesBtn: 'عرض الملفات المنشورة',
    selectedPackageTitle: (refCode: string, verLabel: string, revNum: number) =>
      `ملفات الحزمة المنشورة: ${refCode} · الإصدار ${verLabel} (المراجعة #${revNum})`,
    colFileType: 'نوع الملف',
    colFileName: 'اسم الملف',
    colSizeSha: 'الحجم وبصمة SHA-256',
    downloadBtn: 'تنزيل موثق (SHA-256)',
    downloadSuccessTitle: 'اكتمل التنزيل والتحقق',
    downloadSuccessMsg: (fileName: string, sha256?: string | null) =>
      `تم تنزيل الملف (${fileName}) عبر البوابة الموثقة بنجاح.\nSHA-256: ${sha256 ?? 'VERIFIED'}`,
    errorTitle: 'تعذر تحميل بيانات بوابة المستهلك',
    closeFilesBtn: 'إغلاق القائمة',
    closeModalBtn: 'إغلاق',
    unsetVersion: 'غير محدد بعد',
  },
  en: {
    pageBadge: 'External Consumer Portal · UI-19 / P04',
    title: 'External Consumer Portal & Published Packages',
    subtitle:
      'Displays only active PUBLISHED packages scoped to your assigned Consumer Entity, with server-verified SHA-256 downloads and audit logging.',
    consumerScopeLabel: 'Authorized Consumer Scope',
    emptyIntegrations: 'No active published packages are currently available for your consumer entity.',
    refreshBtn: 'Refresh Portal',
    colRef: 'Reference Code',
    colDoc: 'Document / Provider',
    colActiveVer: 'Active Consumer Version',
    colLatestPub: 'Published Package',
    colFilesCount: 'Files',
    colActions: 'Actions',
    openFilesBtn: 'View Published Files',
    selectedPackageTitle: (refCode: string, verLabel: string, revNum: number) =>
      `Published Package Files: ${refCode} · Version ${verLabel} (Revision #${revNum})`,
    colFileType: 'File Type',
    colFileName: 'File Name',
    colSizeSha: 'Size & SHA-256',
    downloadBtn: 'Verified Download (SHA-256)',
    downloadSuccessTitle: 'Download Verified',
    downloadSuccessMsg: (fileName: string, sha256?: string | null) =>
      `Downloaded (${fileName}) through the consumer portal successfully.\nSHA-256: ${sha256 ?? 'VERIFIED'}`,
    errorTitle: 'Unable to Load Consumer Portal',
    closeFilesBtn: 'Close Files',
    closeModalBtn: 'Close',
    unsetVersion: 'Not assigned',
  },
} as const;

export function ConsumerPortalPage({ lang, onSessionExpired }: ConsumerPortalPageProps) {
  const c = PORTAL_TEXT[lang];
  const commonStrings = UI_STRINGS[lang];

  const [portalData, setPortalData] = useState<PortalIntegrationsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadErrorMsg, setLoadErrorMsg] = useState<string | null>(null);

  const [selectedFiles, setSelectedFiles] = useState<PortalPublicationFilesResponse | null>(null);
  const [filesLoading, setFilesLoading] = useState(false);

  const [dialog, setDialog] = useState<{
    open: boolean;
    variant: 'info' | 'warning' | 'error';
    title: string;
    message: string;
    traceId?: string;
  }>({ open: false, variant: 'info', title: '', message: '' });

  const loadPortal = useCallback(async () => {
    setLoading(true);
    setLoadErrorMsg(null);
    try {
      const res = await getPortalIntegrations(lang);
      setPortalData(res);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setLoadErrorMsg(err instanceof Error ? err.message : c.errorTitle);
    } finally {
      setLoading(false);
    }
  }, [c.errorTitle, lang, onSessionExpired]);

  useEffect(() => {
    void loadPortal();
  }, [loadPortal]);

  async function handleInspectPackage(item: PortalIntegrationItem) {
    setFilesLoading(true);
    try {
      const filesRes = await getPortalPublicationFiles(item.latestPublicationId, lang);
      setSelectedFiles(filesRes);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const message = err instanceof Error ? err.message : c.errorTitle;
      setDialog({
        open: true,
        variant: 'error',
        title: c.errorTitle,
        message,
        traceId: err instanceof ApiProblemError ? err.traceId : undefined,
      });
    } finally {
      setFilesLoading(false);
    }
  }

  async function handleDownloadPortalFile(
    publicationId: string,
    revisionFileId: string,
    originalName: string
  ) {
    try {
      const result = await downloadBinaryFileVerified(
        getPortalPublicationFileDownloadUrl(publicationId, revisionFileId),
        originalName,
        lang
      );
      setDialog({
        open: true,
        variant: 'info',
        title: c.downloadSuccessTitle,
        message: c.downloadSuccessMsg(result.filename, result.sha256),
      });
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const message = err instanceof Error ? err.message : c.errorTitle;
      setDialog({
        open: true,
        variant: 'error',
        title: c.errorTitle,
        message,
        traceId: err instanceof ApiProblemError ? err.traceId : undefined,
      });
    }
  }

  return (
    <section className="surface-card" aria-labelledby="consumer-portal-heading">
      <div className="card-header-row">
        <div>
          <span className="status-badge">{c.pageBadge}</span>
          <h2 id="consumer-portal-heading" className="card-title heading-font" style={{ marginTop: 8 }}>
            {c.title}
          </h2>
          <p className="card-subtitle">{c.subtitle}</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => void loadPortal()} disabled={loading}>
          {c.refreshBtn}
        </Button>
      </div>

      {loading ? (
        <LoadingState title={commonStrings.tableLoadingTitle} />
      ) : loadErrorMsg ? (
        <ErrorState
          title={c.errorTitle}
          message={loadErrorMsg}
          retryLabel={c.refreshBtn}
          onRetry={() => void loadPortal()}
        />
      ) : (
        <>
          {portalData && (
            <div className="slot-stat-box" style={{ marginBottom: 16 }}>
              <div className="slot-stat-label">{c.consumerScopeLabel}</div>
              <div className="slot-stat-value">
                {portalData.consumerEntityName} (
                <code className="ltr-code">{portalData.consumerEntityId}</code>)
              </div>
            </div>
          )}

          {!portalData || portalData.integrations.length === 0 ? (
            <p className="card-subtitle">{c.emptyIntegrations}</p>
          ) : (
            <div className="table-scroll-container">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{c.colRef}</th>
                    <th>{c.colDoc}</th>
                    <th>{c.colActiveVer}</th>
                    <th>{c.colLatestPub}</th>
                    <th>{c.colFilesCount}</th>
                    <th>{c.colActions}</th>
                  </tr>
                </thead>
                <tbody>
                  {portalData.integrations.map((item) => (
                    <tr key={item.integrationId}>
                      <td>
                        <code className="ltr-code">{item.referenceCode}</code>
                      </td>
                      <td>
                        <div>{item.documentTitle}</div>
                        <div className="card-subtitle">
                          {item.providerName} · {item.apiGroupName}
                        </div>
                      </td>
                      <td>
                        {item.activeConsumerVersionLabel ? (
                          <StatusBadge variant="success">{item.activeConsumerVersionLabel}</StatusBadge>
                        ) : (
                          <span>{c.unsetVersion}</span>
                        )}
                      </td>
                      <td>
                        <code className="ltr-code">
                          {item.latestPublicationVersionLabel} · Rev #{item.latestPublicationRevisionNumber}
                        </code>
                      </td>
                      <td>
                        <code className="ltr-code">{item.publishedFilesCount}</code>
                      </td>
                      <td>
                        <Button
                          variant="primary"
                          size="sm"
                          disabled={filesLoading}
                          onClick={() => void handleInspectPackage(item)}
                        >
                          {c.openFilesBtn}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {selectedFiles && (
            <section
              className="surface-card"
              style={{ marginTop: 18, borderStyle: 'dashed' }}
              aria-labelledby="portal-package-files-heading"
            >
              <div className="card-header-row" style={{ marginBottom: 10 }}>
                <h3 id="portal-package-files-heading" className="card-title heading-font">
                  {c.selectedPackageTitle(
                    selectedFiles.referenceCode,
                    selectedFiles.versionLabel,
                    selectedFiles.revisionNumber
                  )}
                </h3>
                <Button variant="outline" size="sm" onClick={() => setSelectedFiles(null)}>
                  {c.closeFilesBtn}
                </Button>
              </div>
              <div className="table-scroll-container">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>{c.colFileType}</th>
                      <th>{c.colFileName}</th>
                      <th>{c.colSizeSha}</th>
                      <th>{c.colActions}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selectedFiles.files.map((file) => (
                      <tr key={file.id}>
                        <td>{file.fileTypeName}</td>
                        <td>{file.originalName}</td>
                        <td>
                          <div>
                            <code className="ltr-code">{file.sizeBytes} bytes</code>
                          </div>
                          <div>
                            <code className="ltr-code" style={{ fontSize: '0.78em' }}>
                              {file.sha256}
                            </code>
                          </div>
                        </td>
                        <td>
                          <Button
                            variant="primary"
                            size="sm"
                            onClick={() =>
                              void handleDownloadPortalFile(
                                selectedFiles.publicationId,
                                file.revisionFileId,
                                file.originalName
                              )
                            }
                          >
                            {c.downloadBtn}
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}

      <CentralModalDialog
        isOpen={dialog.open}
        title={dialog.title}
        message={dialog.message}
        variant={dialog.variant}
        traceId={dialog.traceId}
        confirmLabel={c.closeModalBtn}
        closeAriaLabel={commonStrings.modalCloseAria}
        onConfirm={() => setDialog((d) => ({ ...d, open: false }))}
      />
    </section>
  );
}
