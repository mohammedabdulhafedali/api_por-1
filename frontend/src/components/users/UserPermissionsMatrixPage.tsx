import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  OverrideDecision,
  UserPermissionEntry,
  UserPermissionsMatrixResult,
  getUserPermissionsMatrix,
  replaceUserPermissionOverrides,
} from '../../api/permissionsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import { Button, CentralModalDialog, ErrorState, StandaloneFormPage, StatusBadge } from '../ui';

export interface UserPermissionsMatrixPageProps {
  userId: string;
  lang: AppLanguage;
  canManage: boolean;
  onBack: () => void;
  onSessionExpired: () => void;
  onPermissionsChanged?: () => void;
}

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    badge: 'صلاحيات المستخدم والاستثناءات · UI-15A / I04',
    title: 'مصفوفة الصلاحيات الفردية للمستخدم',
    subtitle:
      'عرض الصلاحيات الموروثة من الأدوار النشطة وتطبيق منح (GRANT) أو سحب (REVOKE) فردي أو الإعادة إلى الموروث وفق قاعدة: السحب الفردي يغلب المنح الفردي ثم اتحاد الأدوار.',
    backBtn: 'العودة إلى قائمة المستخدمين',
    saveBtn: 'حفظ الصلاحيات الفردية',
    cancelBtn: 'إلغاء',
    resetAllBtn: 'إعادة الكل إلى الموروث',
    resetRowBtn: 'إرجاع الصف للموروث',
    activeRolesLabel: 'الأدوار النشطة المسندة:',
    noRolesLabel: 'بلا أدوار نشطة مسندة',
    precedenceBanner:
      'أولوية الاحتساب المعتمدة (ADR-01): السحب الفردي (REVOKE) يغلب دائمًا، يليه المنح الفردي (GRANT)، ثم اتحاد جميع الأدوار النشطة المسندة.',
    readOnlyNotice: 'وضع القراءة فقط: لا تملك صلاحية permissions.manage لتعديل الاستثناءات.',
    statInherited: 'موروثة من الأدوار:',
    statGrant: 'منح فردي (GRANT):',
    statRevoke: 'سحب فردي (REVOKE):',
    statEffective: 'الفعّالة النهائية:',
    screenColHeader: 'الشاشة / المسار',
    sourceInherited: 'موروث',
    sourceGrant: 'منح فردي (GRANT)',
    sourceRevoke: 'سحب فردي (REVOKE)',
    sourceDenied: 'غير ممنوح',
    effectiveAllowed: 'مسموح',
    effectiveDenied: 'ممنوع',
    optInheritAllowed: 'موروث (مسموح)',
    optInheritDenied: 'موروث (ممنوع)',
    optGrant: 'منح فردي (GRANT)',
    optRevoke: 'سحب فردي (REVOKE)',
    confirmSaveTitle: 'تأكيد حفظ الصلاحيات الفردية',
    confirmSaveMsg:
      'سيتم حفظ استثناءات المنح والسحب الفردي في قاعدة البيانات وتطبيقها فورًا على جلسات المستخدم وتسجيلها في سجل التدقيق. هل تؤكد الحفظ؟',
    saveSuccessTitle: 'تم حفظ الصلاحيات الفردية',
    saveSuccessMsg: 'تم تحديث استثناءات صلاحيات المستخدم وتسجيل التغيير في سجل التدقيق بنجاح.',
    loadErrorTitle: 'تعذر تحميل مصفوفة صلاحيات المستخدم',
    saveErrorTitle: 'تعذر حفظ الاستثناءات الفردية',
    loadingText: 'جارٍ تحميل مصفوفة الصلاحيات الفعالة والموروثة...',
    retryBtn: 'إعادة المحاولة',
  },
  en: {
    badge: 'User Effective Permissions & Overrides · UI-15A / I04',
    title: 'User Individual Permissions Matrix',
    subtitle:
      'Inspect active role-inherited permissions and apply individual GRANT, REVOKE, or Inherit decisions (Precedence: User REVOKE > User GRANT > Active Roles Union).',
    backBtn: 'Back to Users Directory',
    saveBtn: 'Save User Overrides',
    cancelBtn: 'Cancel',
    resetAllBtn: 'Reset All to Inherited',
    resetRowBtn: 'Reset Row to Inherited',
    activeRolesLabel: 'Assigned Active Roles:',
    noRolesLabel: 'No active roles assigned',
    precedenceBanner:
      'Approved Precedence (ADR-01): Explicit REVOKE overrides everything, then explicit GRANT, then union of active assigned roles.',
    readOnlyNotice: 'Read-only view: permissions.manage is required to modify individual overrides.',
    statInherited: 'Inherited:',
    statGrant: 'Individual GRANT:',
    statRevoke: 'Individual REVOKE:',
    statEffective: 'Effective Allowed:',
    screenColHeader: 'Screen / Endpoint',
    sourceInherited: 'Inherited',
    sourceGrant: 'User GRANT',
    sourceRevoke: 'User REVOKE',
    sourceDenied: 'Not Granted',
    effectiveAllowed: 'Allowed',
    effectiveDenied: 'Denied',
    optInheritAllowed: 'Inherit (Allowed)',
    optInheritDenied: 'Inherit (Denied)',
    optGrant: 'Explicit GRANT',
    optRevoke: 'Explicit REVOKE',
    confirmSaveTitle: 'Confirm Saving Individual Overrides',
    confirmSaveMsg:
      'Individual GRANT/REVOKE overrides will be persisted to the database, applied immediately on the server, and logged in the audit trail. Continue?',
    saveSuccessTitle: 'User Overrides Saved',
    saveSuccessMsg: 'Individual user permission overrides were updated and recorded in the audit trail.',
    loadErrorTitle: 'Failed to Load User Permissions Matrix',
    saveErrorTitle: 'Failed to Save User Overrides',
    loadingText: 'Loading user inherited & override permissions...',
    retryBtn: 'Retry',
  },
} as const;

type CellOverrideState = 'INHERIT' | OverrideDecision;

export function UserPermissionsMatrixPage({
  userId,
  lang,
  canManage,
  onBack,
  onSessionExpired,
  onPermissionsChanged,
}: UserPermissionsMatrixPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const [matrix, setMatrix] = useState<UserPermissionsMatrixResult | null>(null);
  const [draftOverrides, setDraftOverrides] = useState<Record<string, CellOverrideState>>({});
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);
  const [modal, setModal] = useState<ModalState | null>(null);

  const loadMatrix = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const res = await getUserPermissionsMatrix(userId, lang);
      setMatrix(res);
      const nextDraft: Record<string, CellOverrideState> = {};
      for (const perm of res.permissions) {
        nextDraft[perm.permissionId] = perm.overrideDecision ?? 'INHERIT';
      }
      setDraftOverrides(nextDraft);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const apiErr = err instanceof ApiProblemError ? err : null;
      setLoadError({
        message: apiErr?.message ?? copy.loadErrorTitle,
        traceId: apiErr?.traceId,
      });
    } finally {
      setLoading(false);
    }
  }, [userId, lang, onSessionExpired, copy.loadErrorTitle]);

  useEffect(() => {
    void loadMatrix();
  }, [loadMatrix]);

  const entryByCell = useMemo(() => {
    const map = new Map<string, UserPermissionEntry>();
    if (!matrix) return map;
    for (const perm of matrix.permissions) {
      map.set(`${perm.screenId}::${perm.columnId}`, perm);
    }
    return map;
  }, [matrix]);

  const summaryStats = useMemo(() => {
    if (!matrix) {
      return { inherited: 0, grants: 0, revokes: 0, effective: 0 };
    }
    let inherited = 0;
    let grants = 0;
    let revokes = 0;
    let effective = 0;
    for (const perm of matrix.permissions) {
      if (perm.isInherited) inherited++;
      const state = draftOverrides[perm.permissionId] ?? 'INHERIT';
      if (state === 'GRANT') grants++;
      else if (state === 'REVOKE') revokes++;

      const isAllowed = state === 'REVOKE' ? false : state === 'GRANT' ? true : perm.isInherited;
      if (isAllowed) effective++;
    }
    return { inherited, grants, revokes, effective };
  }, [matrix, draftOverrides]);

  const groups = useMemo(() => {
    if (!matrix) return [];
    const list: Array<{
      groupId: string;
      groupLabel: string;
      screens: UserPermissionsMatrixResult['screens'];
    }> = [];
    for (const scr of matrix.screens) {
      const label = lang === 'ar' ? scr.groupLabelAr : scr.groupLabelEn;
      let existing = list.find((g) => g.groupId === scr.groupId);
      if (!existing) {
        existing = { groupId: scr.groupId, groupLabel: label, screens: [] };
        list.push(existing);
      }
      existing.screens.push(scr);
    }
    return list;
  }, [matrix, lang]);

  const handleResetAllToInherited = () => {
    if (!matrix) return;
    const resetDraft: Record<string, CellOverrideState> = {};
    for (const perm of matrix.permissions) {
      resetDraft[perm.permissionId] = 'INHERIT';
    }
    setDraftOverrides(resetDraft);
  };

  const handleResetRowToInherited = (screenId: string) => {
    if (!matrix) return;
    setDraftOverrides((prev) => {
      const next = { ...prev };
      for (const perm of matrix.permissions) {
        if (perm.screenId === screenId) {
          next[perm.permissionId] = 'INHERIT';
        }
      }
      return next;
    });
  };

  const executeSave = async () => {
    if (!matrix) return;
    const overridesToSave: Array<{ permissionId: string; decision: OverrideDecision }> = [];
    for (const perm of matrix.permissions) {
      const state = draftOverrides[perm.permissionId] ?? 'INHERIT';
      if (state === 'GRANT' || state === 'REVOKE') {
        overridesToSave.push({ permissionId: perm.permissionId, decision: state });
      }
    }

    try {
      const updated = await replaceUserPermissionOverrides(matrix.userId, overridesToSave, lang);
      setMatrix(updated);
      const nextDraft: Record<string, CellOverrideState> = {};
      for (const perm of updated.permissions) {
        nextDraft[perm.permissionId] = perm.overrideDecision ?? 'INHERIT';
      }
      setDraftOverrides(nextDraft);
      setModal({
        variant: 'success',
        title: copy.saveSuccessTitle,
        message: copy.saveSuccessMsg,
      });
      onPermissionsChanged?.();
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const apiErr = err instanceof ApiProblemError ? err : null;
      setModal({
        variant: 'error',
        title: apiErr?.title ?? copy.saveErrorTitle,
        message: apiErr?.message ?? copy.saveErrorTitle,
      });
    }
  };

  if (loading) {
    return (
      <section className="surface-card">
        <p>{copy.loadingText}</p>
      </section>
    );
  }

  if (loadError || !matrix) {
    return (
      <section className="surface-card">
        <ErrorState
          title={copy.loadErrorTitle}
          message={loadError?.message ?? copy.loadErrorTitle}
          traceId={loadError?.traceId}
          retryLabel={copy.retryBtn}
          onRetry={() => void loadMatrix()}
        />
        <div style={{ marginTop: 12 }}>
          <Button variant="outline" onClick={onBack}>
            {copy.backBtn}
          </Button>
        </div>
      </section>
    );
  }

  return (
    <>
      <StandaloneFormPage
        modeBadgeLabel={copy.badge}
        formTitle={`${copy.title}: ${matrix.fullName}`}
        formSubtitle={copy.subtitle}
        backButtonLabel={copy.backBtn}
        submitButtonLabel={canManage ? copy.saveBtn : copy.backBtn}
        cancelButtonLabel={copy.cancelBtn}
        onBack={onBack}
        onCancel={onBack}
        onSubmit={() => {
          if (!canManage) {
            onBack();
            return;
          }
          setModal({
            variant: 'confirm',
            title: copy.confirmSaveTitle,
            message: copy.confirmSaveMsg,
            onConfirmAction: executeSave,
          });
        }}
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div
            style={{
              padding: 12,
              borderRadius: 8,
              border: '1px solid var(--border-color)',
              background: 'var(--bg-subtle)',
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 10,
            }}
          >
            <div>
              <div style={{ fontWeight: 600 }}>{matrix.fullName}</div>
              <div className="ltr-code" style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                {matrix.email}
              </div>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>{copy.activeRolesLabel}</span>
              {matrix.activeRoles.length > 0 ? (
                matrix.activeRoles.map((r) => (
                  <StatusBadge key={r.id} variant="info">
                    {r.name}
                  </StatusBadge>
                ))
              ) : (
                <StatusBadge variant="neutral">{copy.noRolesLabel}</StatusBadge>
              )}
            </div>
          </div>

          <p style={{ fontSize: 13, color: 'var(--text-muted)', margin: 0 }}>
            {copy.precedenceBanner}
            {!canManage ? ` — ${copy.readOnlyNotice}` : ''}
          </p>

          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 10,
            }}
          >
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
              <StatusBadge variant="neutral">
                {copy.statInherited} {summaryStats.inherited}
              </StatusBadge>
              <StatusBadge variant="success">
                {copy.statGrant} {summaryStats.grants}
              </StatusBadge>
              <StatusBadge variant="danger">
                {copy.statRevoke} {summaryStats.revokes}
              </StatusBadge>
              <StatusBadge variant="info">
                {copy.statEffective} {summaryStats.effective}
              </StatusBadge>
            </div>
            {canManage && (
              <Button variant="outline" size="sm" onClick={handleResetAllToInherited}>
                {copy.resetAllBtn}
              </Button>
            )}
          </div>

          <div className="permission-matrix-scroll matrix-scroll-container">
            <table className="permission-matrix selection-matrix">
              <thead>
                <tr>
                  <th className="perm-screen matrix-sticky-col matrix-corner-header">{copy.screenColHeader}</th>
                  {matrix.columns.map((col) => (
                    <th key={col.id} className="matrix-col-header">
                      <span className="col-title matrix-col-title">
                        {lang === 'ar' ? col.labelAr : col.labelEn}
                      </span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {groups.map((group) => (
                  <Fragment key={group.groupId}>
                    <tr className="perm-group matrix-group-row">
                      <td colSpan={matrix.columns.length + 1}>
                        <div className="matrix-group-bar">
                          <span className="section-name heading-font">{group.groupLabel}</span>
                        </div>
                      </td>
                    </tr>
                    {group.screens.map((screenRow) => {
                      const screenTitle = lang === 'ar' ? screenRow.titleAr : screenRow.titleEn;
                      return (
                        <tr key={screenRow.id}>
                          <td className="perm-screen matrix-sticky-col matrix-row-header">
                            <div className="all-row-toggle matrix-header-row-between" style={{ gap: 8 }}>
                              <div className="row-title">
                                <b className="matrix-row-title">{screenTitle}</b>
                                <span className="perm-path matrix-row-path ltr-code">
                                  {screenRow.technicalPath}
                                </span>
                              </div>
                              {canManage && (
                                <Button
                                  variant="outline"
                                  size="sm"
                                  onClick={() => handleResetRowToInherited(screenRow.id)}
                                >
                                  {copy.resetRowBtn}
                                </Button>
                              )}
                            </div>
                          </td>
                          {matrix.columns.map((col) => {
                            const entry = entryByCell.get(`${screenRow.id}::${col.id}`);
                            if (!entry) {
                              return (
                                <td key={col.id} className="not-applicable matrix-cell-na" aria-disabled="true">
                                  —
                                </td>
                              );
                            }

                            const state = draftOverrides[entry.permissionId] ?? 'INHERIT';
                            const effectiveAllowed =
                              state === 'REVOKE'
                                ? false
                                : state === 'GRANT'
                                  ? true
                                  : entry.isInherited;

                            const sourceLabel =
                              state === 'REVOKE'
                                ? copy.sourceRevoke
                                : state === 'GRANT'
                                  ? copy.sourceGrant
                                  : entry.isInherited
                                    ? `${copy.sourceInherited}: ${entry.inheritedFromRoles
                                        .map((r) => r.name)
                                        .join(', ')}`
                                    : copy.sourceDenied;

                            const colLabel = lang === 'ar' ? col.labelAr : col.labelEn;

                            return (
                              <td
                                key={col.id}
                                className={`matrix-cell ${state !== 'INHERIT' ? 'perm-override' : 'perm-inherited'}`}
                                style={{ minWidth: 145, padding: '8px 6px' }}
                              >
                                <div
                                  style={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    gap: 4,
                                    alignItems: 'center',
                                  }}
                                >
                                  <StatusBadge
                                    variant={effectiveAllowed ? 'success' : 'danger'}
                                  >
                                    {effectiveAllowed ? copy.effectiveAllowed : copy.effectiveDenied}
                                  </StatusBadge>
                                  <select
                                    aria-label={`${screenTitle} - ${colLabel}`}
                                    disabled={!canManage}
                                    value={state}
                                    onChange={(e) => {
                                      const val = e.target.value as CellOverrideState;
                                      setDraftOverrides((prev) => ({
                                        ...prev,
                                        [entry.permissionId]: val,
                                      }));
                                    }}
                                    style={{
                                      fontSize: 11.5,
                                      padding: '3px 6px',
                                      borderRadius: 6,
                                      border: '1px solid var(--border-color)',
                                      background: 'var(--bg-input)',
                                      color: 'var(--text-primary)',
                                      maxWidth: 140,
                                    }}
                                  >
                                    <option value="INHERIT">
                                      {entry.isInherited
                                        ? copy.optInheritAllowed
                                        : copy.optInheritDenied}
                                    </option>
                                    <option value="GRANT">{copy.optGrant}</option>
                                    <option value="REVOKE">{copy.optRevoke}</option>
                                  </select>
                                  <span
                                    style={{
                                      fontSize: 10.5,
                                      color: 'var(--text-muted)',
                                      textAlign: 'center',
                                    }}
                                  >
                                    {sourceLabel}
                                  </span>
                                </div>
                              </td>
                            );
                          })}
                        </tr>
                      );
                    })}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </StandaloneFormPage>

      <CentralModalDialog
        isOpen={modal !== null}
        variant={modal?.variant ?? 'info'}
        title={modal?.title ?? ''}
        message={modal?.message ?? ''}
        confirmLabel={t.modalConfirmBtn}
        cancelLabel={modal?.variant === 'confirm' ? t.modalCancelBtn : undefined}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={() => {
          const active = modal;
          if (!active) return;
          if (active.variant === 'confirm' && active.onConfirmAction) {
            void active.onConfirmAction();
            return;
          }
          setModal(null);
        }}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}

