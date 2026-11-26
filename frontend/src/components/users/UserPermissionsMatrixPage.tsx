import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  OverrideDecision,
  UserPermissionEntry,
  UserPermissionsMatrixResult,
  getUserPermissionsMatrix,
  replaceUserPermissionOverrides,
} from '../../api/permissionsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import { resolveBulkColumnLabel, resolvePermissionChipLabel } from '../../utils/permissionLabels';
import {
  BackIconButton,
  Button,
  CentralModalDialog,
  ErrorState,
  PermissionBoardItem,
  PermissionBoardScreen,
  PermissionsCardsBoard,
  StandaloneFormPage,
  StatusBadge,
} from '../ui';

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

const REMOVED_SCREEN_IDS = new Set(['UI-18', 'UI-19', 'LEGACY_COMPOUND']);
const REMOVED_PERM_CODES = new Set([
  'search.view',
  'portal.view',
  'consumers.manage',
  'api_groups.manage',
  'file_types.manage',
  'requirements.manage',
  'routes.manage',
  'changes.config.manage',
]);

const COPY = {
  ar: {
    badge: 'صلاحيات المستخدم',
    title: 'صلاحيات المستخدم الفردية',
    subtitle: '',
    backBtn: 'العودة',
    saveBtn: 'حفظ',
    cancelBtn: 'إلغاء',
    resetAllBtn: 'إعادة الكل إلى الموروث',
    activeRolesLabel: 'الأدوار النشطة المسندة:',
    noRolesLabel: 'بلا أدوار نشطة مسندة',
    readOnlyNotice: 'وضع القراءة فقط: لا تملك صلاحية permissions.manage لتعديل الاستثناءات.',
    statInherited: 'موروثة من الأدوار:',
    statGrant: 'منح فردي (GRANT):',
    statRevoke: 'سحب فردي (REVOKE):',
    statEffective: 'الفعّالة النهائية:',
    sourceInherited: 'موروث',
    sourceDenied: 'غير ممنوح',
    grantTag: 'منح فردي',
    revokeTag: 'سحب فردي',
    confirmSaveTitle: 'تأكيد حفظ الصلاحيات الفردية',
    confirmSaveMsg:
      'سيتم حفظ استثناءات المنح والسحب الفردي في قاعدة البيانات وتطبيقها فورًا على جلسات المستخدم وتسجيلها في سجل التدقيق. هل تؤكد الحفظ؟',
    saveSuccessTitle: 'تم حفظ الصلاحيات الفردية',
    saveSuccessMsg: 'تم تحديث استثناءات صلاحيات المستخدم وتسجيل التغيير في سجل التدقيق بنجاح.',
    loadErrorTitle: 'تعذر تحميل صلاحيات المستخدم',
    saveErrorTitle: 'تعذر حفظ الاستثناءات الفردية',
    loadingText: 'جارٍ تحميل الصلاحيات الفعالة والموروثة...',
    retryBtn: 'إعادة المحاولة',
  },
  en: {
    badge: 'User Permissions',
    title: 'User Individual Permissions',
    subtitle: '',
    backBtn: 'Back',
    saveBtn: 'Save',
    cancelBtn: 'Cancel',
    resetAllBtn: 'Reset All to Inherited',
    activeRolesLabel: 'Assigned Active Roles:',
    noRolesLabel: 'No active roles assigned',
    readOnlyNotice: 'Read-only view: permissions.manage is required to modify individual overrides.',
    statInherited: 'Inherited:',
    statGrant: 'Individual GRANT:',
    statRevoke: 'Individual REVOKE:',
    statEffective: 'Effective Allowed:',
    sourceInherited: 'Inherited',
    sourceDenied: 'Not Granted',
    grantTag: 'Grant',
    revokeTag: 'Revoke',
    confirmSaveTitle: 'Confirm Saving Individual Overrides',
    confirmSaveMsg:
      'Individual GRANT/REVOKE overrides will be persisted to the database, applied immediately on the server, and logged in the audit trail. Continue?',
    saveSuccessTitle: 'User Overrides Saved',
    saveSuccessMsg: 'Individual user permission overrides were updated and recorded in the audit trail.',
    loadErrorTitle: 'Failed to Load User Permissions',
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

  const activePermissions = useMemo(() => {
    if (!matrix) return [];
    return matrix.permissions.filter(
      (p) =>
        !REMOVED_SCREEN_IDS.has(p.screenId) &&
        !REMOVED_PERM_CODES.has(p.code.toLowerCase())
    );
  }, [matrix]);

  const activeScreens = useMemo(() => {
    if (!matrix) return [];
    return matrix.screens.filter((s) => !REMOVED_SCREEN_IDS.has(s.id));
  }, [matrix]);

  const permById = useMemo(() => {
    const map = new Map<string, UserPermissionEntry>();
    for (const p of activePermissions) {
      map.set(p.permissionId, p);
    }
    return map;
  }, [activePermissions]);

  const colLabelById = useMemo(() => {
    const map = new Map<string, string>();
    if (!matrix) return map;
    for (const col of matrix.columns) {
      map.set(col.id, lang === 'ar' ? col.labelAr : col.labelEn);
    }
    return map;
  }, [matrix, lang]);

  const summaryStats = useMemo(() => {
    let inherited = 0;
    let grants = 0;
    let revokes = 0;
    let effective = 0;
    for (const perm of activePermissions) {
      if (perm.isInherited) inherited++;
      const state = draftOverrides[perm.permissionId] ?? 'INHERIT';
      if (state === 'GRANT') grants++;
      else if (state === 'REVOKE') revokes++;

      const isAllowed = state === 'REVOKE' ? false : state === 'GRANT' ? true : perm.isInherited;
      if (isAllowed) effective++;
    }
    return { inherited, grants, revokes, effective };
  }, [activePermissions, draftOverrides]);

  const setPermissionsChecked = (perms: UserPermissionEntry[], checked: boolean) => {
    setDraftOverrides((prev) => {
      const next = { ...prev };
      for (const perm of perms) {
        if (checked) {
          next[perm.permissionId] = perm.isInherited ? 'INHERIT' : 'GRANT';
        } else {
          next[perm.permissionId] = perm.isInherited ? 'REVOKE' : 'INHERIT';
        }
      }
      return next;
    });
  };

  const handleResetItemToInherited = (permissionId: string) => {
    setDraftOverrides((prev) => ({
      ...prev,
      [permissionId]: 'INHERIT',
    }));
  };

  const handleResetAllToInherited = () => {
    if (!matrix) return;
    const resetDraft: Record<string, CellOverrideState> = {};
    for (const perm of matrix.permissions) {
      resetDraft[perm.permissionId] = 'INHERIT';
    }
    setDraftOverrides(resetDraft);
  };

  const handleResetRowToInherited = (screenId: string) => {
    setDraftOverrides((prev) => {
      const next = { ...prev };
      for (const perm of activePermissions) {
        if (perm.screenId === screenId) {
          next[perm.permissionId] = 'INHERIT';
        }
      }
      return next;
    });
  };

  const boardScreens: PermissionBoardScreen[] = useMemo(() => {
    return activeScreens
      .map((scr) => {
        const rowPerms = activePermissions.filter((p) => p.screenId === scr.id);
        const items: PermissionBoardItem[] = rowPerms.map((entry) => {
          const state = draftOverrides[entry.permissionId] ?? 'INHERIT';
          const effectiveAllowed =
            state === 'REVOKE' ? false : state === 'GRANT' ? true : entry.isInherited;
          const label = resolvePermissionChipLabel(
            entry.code,
            entry.columnId,
            colLabelById.get(entry.columnId) ?? entry.action,
            lang
          );

          if (state === 'GRANT') {
            return {
              id: entry.permissionId,
              columnId: entry.columnId,
              label,
              checked: effectiveAllowed,
              mode: 'grant',
              statusTag: copy.grantTag,
              title: copy.grantTag,
              canReset: true,
            };
          }

          if (state === 'REVOKE') {
            return {
              id: entry.permissionId,
              columnId: entry.columnId,
              label,
              checked: effectiveAllowed,
              mode: 'revoke',
              statusTag: copy.revokeTag,
              title: copy.revokeTag,
              canReset: true,
            };
          }

          return {
            id: entry.permissionId,
            columnId: entry.columnId,
            label,
            checked: effectiveAllowed,
            mode: 'inherited',
            statusTag: entry.isInherited ? copy.sourceInherited : undefined,
            title: entry.isInherited ? copy.sourceInherited : copy.sourceDenied,
            canReset: false,
          };
        });

        const hasOverrides = rowPerms.some(
          (p) => (draftOverrides[p.permissionId] ?? 'INHERIT') !== 'INHERIT'
        );

        return {
          id: scr.id,
          groupId: scr.groupId,
          groupLabel: lang === 'ar' ? scr.groupLabelAr : scr.groupLabelEn,
          title: lang === 'ar' ? scr.titleAr : scr.titleEn,
          items,
          hasOverrides,
        };
      })
      .filter((s) => s.items.length > 0);
  }, [activeScreens, activePermissions, draftOverrides, colLabelById, lang, copy]);

  const boardActionColumns = useMemo(() => {
    if (!matrix) return [];
    const usedColIds = new Set(activePermissions.map((p) => p.columnId));
    return matrix.columns
      .filter((c) => usedColIds.has(c.id))
      .map((c) => ({
        id: c.id,
        label: resolveBulkColumnLabel(c.id, lang === 'ar' ? c.labelAr : c.labelEn, lang),
      }));
  }, [matrix, activePermissions, lang]);

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
          <BackIconButton label={copy.backBtn} onClick={onBack} />
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
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
          <div
            style={{
              padding: 12,
              borderRadius: 10,
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
              <div style={{ fontWeight: 700 }}>{matrix.fullName}</div>
              <div className="ltr-code" style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                {matrix.email}
              </div>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                {copy.activeRolesLabel}
              </span>
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

          {!canManage && (
            <p style={{ fontSize: 13, color: 'var(--text-muted)', margin: 0 }}>
              {copy.readOnlyNotice}
            </p>
          )}

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
          </div>

          <PermissionsCardsBoard
            lang={lang}
            disabled={!canManage}
            screens={boardScreens}
            actionColumns={boardActionColumns}
            showOverridesFilter
            onToggleItem={(permId, checked) => {
              const entry = permById.get(permId);
              if (entry) setPermissionsChecked([entry], checked);
            }}
            onResetItem={handleResetItemToInherited}
            onToggleScreen={(screenId, checked) => {
              setPermissionsChecked(
                activePermissions.filter((p) => p.screenId === screenId),
                checked
              );
            }}
            onResetScreen={handleResetRowToInherited}
            onToggleGroup={(groupId, checked) => {
              setPermissionsChecked(
                activePermissions.filter((p) => p.groupId === groupId),
                checked
              );
            }}
            onToggleAll={(checked) => {
              setPermissionsChecked(activePermissions, checked);
            }}
            extraToolbarActions={
              canManage ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={handleResetAllToInherited}
                >
                  {copy.resetAllBtn}
                </Button>
              ) : undefined
            }
          />
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
