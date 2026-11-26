import { DOCUMENT_GROUPS } from '../../utils/documentPermissionGroups';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  DocumentAccessMatrixResponse,
  DocumentAclDecision,
  getDocumentAccessMatrix,
  replaceDocumentAccessMatrix,
} from '../../api/documentsAndIntegrationsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  BackIconButton,
  Button,
  CentralModalDialog,
  ErrorState,
  LoadingState,
  StatusBadge,
} from '../ui';

interface DocumentAccessMatrixPageProps {
  lang: AppLanguage;
  documentId: string;
  canManageAcl: boolean;
  onBack: () => void;
  onSessionExpired: () => void;
  onAccessUpdated?: () => void;
}

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    badge: 'مصفوفة صلاحيات الوثيقة',
    titlePrefix: 'صلاحيات الوصول للوثيقة:',
    subtitle:
      'الوثائق مقيدة افتراضيًا: الأولوية المعتمدة هي سحب المستخدم > منح المستخدم > سحب أي دور > منح دور > المنع الافتراضي.',
    backBtn: 'العودة إلى قائمة الوثائق',
    saveBtn: 'حفظ صلاحيات الوثيقة',
    savingBtn: 'جارٍ الحفظ...',
    creatorLabel: 'منشئ الوثيقة:',
    groupLabel: 'مجموعة API:',
    rolesSectionTitle: '1. صلاحيات الأدوار على هذه الوثيقة',
    usersSectionTitle: '2. مصفوفة المستخدمين والنتيجة الفعلية',
    roleCol: 'الدور التنظيمي',
    userCol: 'المستخدم والأدوار والتكليف',
    optInherit: '— (افتراضي)',
    optGrant: 'منح (GRANT)',
    optRevoke: 'سحب (REVOKE)',
    assignedDevBadge: 'مطوّر مكلّف',
    effectiveAllow: 'مسموح',
    effectiveDeny: 'ممنوع',
    srcUserGrant: 'منح فردي',
    srcUserRevoke: 'سحب فردي',
    srcRoleConflict: 'سحب دور يغلب',
    srcRoleRevoke: 'سحب دور',
    srcRoleGrant: 'منح دور',
    srcDefaultDeny: 'ممنوع افتراضيًا',
    grantAllRow: 'منح الصف',
    clearRow: 'إعادة للافتراضي',
    confirmSaveTitle: 'تأكيد حفظ مصفوفة صلاحيات الوثيقة',
    confirmSaveMsg: 'سيتم تطبيق قواعد المنح والسحب الفردية وقواعد الأدوار فورًا على الخادم وتسجيلها في سجل التدقيق.',
    savedTitle: 'تم تحديث صلاحيات الوثيقة',
    savedMsg: 'تم حفظ مصفوفة صلاحيات الوثيقة بنجاح وتحديث التقييم الفعلي.',
    loadErrTitle: 'تعذر تحميل مصفوفة صلاحيات الوثيقة',
  },
  en: {
    badge: 'Document Access Matrix',
    titlePrefix: 'Document Access Control:',
    subtitle:
      'Restricted by default. Precedence: User REVOKE > User GRANT > Role REVOKE > Role GRANT > Default Deny.',
    backBtn: 'Back to API Documents',
    saveBtn: 'Save Document ACL',
    savingBtn: 'Saving...',
    creatorLabel: 'Creator:',
    groupLabel: 'API Group:',
    rolesSectionTitle: '1. Role Permissions on This Document',
    usersSectionTitle: '2. User x Document Action Matrix',
    roleCol: 'Role',
    userCol: 'User, Roles & Assignment',
    optInherit: '— (Default)',
    optGrant: 'GRANT',
    optRevoke: 'REVOKE',
    assignedDevBadge: 'Assigned Dev',
    effectiveAllow: 'Allowed',
    effectiveDeny: 'Denied',
    srcUserGrant: 'User GRANT',
    srcUserRevoke: 'User REVOKE',
    srcRoleConflict: 'Role REVOKE wins',
    srcRoleRevoke: 'Role REVOKE',
    srcRoleGrant: 'Role GRANT',
    srcDefaultDeny: 'Default Deny',
    grantAllRow: 'Grant Row',
    clearRow: 'Reset Row',
    confirmSaveTitle: 'Confirm Document ACL Update',
    confirmSaveMsg: 'Role and user GRANT/REVOKE rules will be enforced immediately on the server and audited.',
    savedTitle: 'Document ACL Saved',
    savedMsg: 'Document access control matrix saved and effective permissions recalculated.',
    loadErrTitle: 'Failed to Load Document Access Matrix',
  },
} as const;

export function DocumentAccessMatrixPage({
  lang,
  documentId,
  canManageAcl,
  onBack,
  onSessionExpired,
  onAccessUpdated,
}: DocumentAccessMatrixPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];

  const [actionGroup, setActionGroup] = useState('document');
  const [matrix, setMatrix] = useState<DocumentAccessMatrixResponse | null>(null);
  const visiblePermissions = matrix?.permissions.filter(p => DOCUMENT_GROUPS.find(g => g.id === actionGroup)?.codes.includes(p.code)) ?? [];
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [modal, setModal] = useState<ModalState | null>(null);

  // Draft dictionaries: roleId -> { permCode: 'GRANT'|'REVOKE'|null }, userId -> { permCode: 'GRANT'|'REVOKE'|null }
  const [roleDraft, setRoleDraft] = useState<Record<string, Record<string, DocumentAclDecision | null>>>({});
  const [userDraft, setUserDraft] = useState<Record<string, Record<string, DocumentAclDecision | null>>>({});

  const hydrateDrafts = (res: DocumentAccessMatrixResponse) => {
    const rDraft: Record<string, Record<string, DocumentAclDecision | null>> = {};
    for (const r of res.roles) {
      rDraft[r.roleId] = { ...r.decisions };
    }
    const uDraft: Record<string, Record<string, DocumentAclDecision | null>> = {};
    for (const u of res.users) {
      const cellMap: Record<string, DocumentAclDecision | null> = {};
      for (const col of res.permissions) {
        cellMap[col.code] = u.cells[col.code]?.explicitDecision ?? null;
      }
      uDraft[u.userId] = cellMap;
    }
    setRoleDraft(rDraft);
    setUserDraft(uDraft);
  };

  const loadMatrix = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const res = await getDocumentAccessMatrix(documentId, lang);
      setMatrix(res);
      hydrateDrafts(res);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg(err instanceof Error ? err.message : c.loadErrTitle);
    } finally {
      setLoading(false);
    }
  }, [c.loadErrTitle, documentId, lang, onSessionExpired]);

  useEffect(() => {
    void loadMatrix();
  }, [loadMatrix]);

  const roleIdsByUserId = useMemo(() => {
    if (!matrix) return {};
    const nameToId = new Map(matrix.roles.map((r) => [r.roleName, r.roleId]));
    const map: Record<string, string[]> = {};
    for (const u of matrix.users) {
      map[u.userId] = u.roleNames.map((n) => nameToId.get(n)).filter((id): id is string => Boolean(id));
    }
    return map;
  }, [matrix]);

  // Client preview mirrors server ADR-01 precedence for live matrix feedback; server re-validates & computes truth.
  const evaluatePreviewCell = (userId: string, permCode: string) => {
    const explicit = userDraft[userId]?.[permCode] ?? null;
    if (explicit === 'REVOKE') return { allowed: false, label: c.srcUserRevoke };
    if (explicit === 'GRANT') return { allowed: true, label: c.srcUserGrant };

    const rIds = roleIdsByUserId[userId] ?? [];
    const roleDecisions = rIds
      .map((rid) => roleDraft[rid]?.[permCode] ?? null)
      .filter((d): d is DocumentAclDecision => d !== null);

    const hasRevoke = roleDecisions.includes('REVOKE');
    const hasGrant = roleDecisions.includes('GRANT');
    if (hasRevoke) {
      return { allowed: false, label: hasGrant ? c.srcRoleConflict : c.srcRoleRevoke };
    }
    if (hasGrant) {
      return { allowed: true, label: c.srcRoleGrant };
    }
    return { allowed: false, label: c.srcDefaultDeny };
  };

  const toggleUserDocCheckbox = (userId: string, code: string, checked: boolean) => {
    setUserCell(userId, code, checked ? 'GRANT' : 'REVOKE');
  };

  const setRoleCell = (roleId: string, code: string, decision: DocumentAclDecision | null) => {
    setRoleDraft((prev) => ({
      ...prev,
      [roleId]: { ...(prev[roleId] ?? {}), [code]: decision },
    }));
  };

  const setUserCell = (userId: string, code: string, decision: DocumentAclDecision | null) => {
    setUserDraft((prev) => ({
      ...prev,
      [userId]: { ...(prev[userId] ?? {}), [code]: decision },
    }));
  };

  const setAllUserRow = (userId: string, decision: DocumentAclDecision | null) => {
    if (!matrix) return;
    const rowMap: Record<string, DocumentAclDecision | null> = {};
    for (const col of visiblePermissions) {
      rowMap[col.code] = decision;
    }
    setUserDraft((prev) => ({ ...prev, [userId]: { ...prev[userId], ...rowMap } }));
  };

  const setAllRoleRow = (roleId: string, decision: DocumentAclDecision | null) => {
    if (!matrix) return;
    const rowMap: Record<string, DocumentAclDecision | null> = {};
    for (const col of visiblePermissions) {
      rowMap[col.code] = decision;
    }
    setRoleDraft((prev) => ({ ...prev, [roleId]: { ...prev[roleId], ...rowMap } }));
  };

  const performSave = async () => {
    if (!matrix) return;
    const rolePermissions: Array<{ roleId: string; permissionCode: string; decision: DocumentAclDecision }> = [];
    for (const r of matrix.roles) {
      for (const col of matrix.permissions) {
        const val = roleDraft[r.roleId]?.[col.code];
        if (val === 'GRANT' || val === 'REVOKE') {
          rolePermissions.push({ roleId: r.roleId, permissionCode: col.code, decision: val });
        }
      }
    }

    const userPermissions: Array<{ userId: string; permissionCode: string; decision: DocumentAclDecision }> = [];
    for (const u of matrix.users) {
      for (const col of matrix.permissions) {
        const val = userDraft[u.userId]?.[col.code];
        if (val === 'GRANT' || val === 'REVOKE') {
          userPermissions.push({ userId: u.userId, permissionCode: col.code, decision: val });
        }
      }
    }

    setSaving(true);
    try {
      const updated = await replaceDocumentAccessMatrix(
        documentId,
        { rolePermissions, userPermissions },
        lang
      );
      setMatrix(updated);
      hydrateDrafts(updated);
      setModal({ variant: 'success', title: c.savedTitle, message: c.savedMsg });
      onAccessUpdated?.();
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setModal({
        variant: 'error',
        title: c.loadErrTitle,
        message: err instanceof Error ? err.message : c.loadErrTitle,
      });
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <section className="surface-card">
        <LoadingState title={commonStrings.tableLoadingTitle} />
      </section>
    );
  }

  if (errorMsg || !matrix) {
    return (
      <section className="surface-card">
        <div className="button-row" style={{ marginBottom: 12 }}>
          <BackIconButton label={c.backBtn} onClick={onBack} />
        </div>
        <ErrorState
          title={c.loadErrTitle}
          message={errorMsg ?? c.loadErrTitle}
          retryLabel={commonStrings.tableRetryBtn}
          onRetry={() => void loadMatrix()}
        />
      </section>
    );
  }

  return (
    <>
      <section className="surface-card">
        <div className="card-header-row">
          <div>
            <h2 className="card-title heading-font">
              {c.titlePrefix} {matrix.derivedTitle}
            </h2>
            <p className="card-subtitle">
              {c.groupLabel} <strong>{matrix.apiGroupName}</strong> · {c.creatorLabel}{' '}
              <strong>{matrix.createdByName}</strong>
            </p>
          </div>
          <div className="button-row">
            <BackIconButton label={c.backBtn} onClick={onBack} />
            {canManageAcl && (
              <Button
                variant="primary"
                disabled={saving}
                onClick={() =>
                  setModal({
                    variant: 'confirm',
                    title: c.confirmSaveTitle,
                    message: c.confirmSaveMsg,
                    onConfirmAction: performSave,
                  })
                }
              >
                {saving ? c.savingBtn : c.saveBtn}
              </Button>
            )}
          </div>
        </div>

        <div className="permissions-groups" aria-label={lang === 'ar' ? 'مجموعات الصلاحيات' : 'Permission groups'}>
          {DOCUMENT_GROUPS.map(group => <button type="button" key={group.id} aria-pressed={actionGroup === group.id}
            onClick={() => setActionGroup(group.id)}>{lang === 'ar' ? group.ar : group.en}</button>)}
        </div>
        {/* 1. Roles Document ACL Matrix */}
        <h3 className="card-title heading-font" style={{ marginTop: 18, marginBottom: 10 }}>
          {c.rolesSectionTitle}
        </h3>
        <div className="permission-matrix-scroll acl-scroll matrix-scroll-wrapper" style={{ marginBottom: 24 }}>
          <table className="data-table acl-table selection-matrix-table">
            <thead>
              <tr>
                <th className="matrix-sticky-col">{c.roleCol}</th>
                {visiblePermissions.map((col) => (
                  <th key={`rh-${col.code}`}>
                    <div>{lang === 'ar' ? col.labelAr : col.labelEn}</div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {matrix.roles.map((role) => (
                <tr key={role.roleId}>
                  <td className="matrix-sticky-col">
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      <strong>{role.roleName}</strong>
                      {canManageAcl && (
                        <div className="button-row" style={{ gap: 4 }}>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setAllRoleRow(role.roleId, 'GRANT')}
                          >
                            {c.grantAllRow}
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setAllRoleRow(role.roleId, null)}
                          >
                            {c.clearRow}
                          </Button>
                        </div>
                      )}
                    </div>
                  </td>
                  {visiblePermissions.map((col) => {
                    const current = roleDraft[role.roleId]?.[col.code] ?? null;
                    const isChecked = current === 'GRANT';
                    return (
                      <td key={`${role.roleId}-${col.code}`}>
                        <input
                          type="checkbox"
                          className="perm-check"
                          aria-label={`${role.roleName} — ${lang === 'ar' ? col.labelAr : col.labelEn}`}
                          disabled={!canManageAcl}
                          checked={isChecked}
                          onChange={(e) =>
                            setRoleCell(role.roleId, col.code, e.target.checked ? 'GRANT' : 'REVOKE')
                          }
                        />
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* 2. Users Complete Document Permissions Matrix (U02) */}
        <h3 className="card-title heading-font" style={{ marginTop: 18, marginBottom: 10 }}>
          {c.usersSectionTitle}
        </h3>
        <div className="permission-matrix-scroll acl-scroll matrix-scroll-wrapper">
          <table className="data-table acl-table selection-matrix-table">
            <thead>
              <tr>
                <th className="matrix-sticky-col">{c.userCol}</th>
                {visiblePermissions.map((col) => (
                  <th key={`uh-${col.code}`}>
                    <div>{lang === 'ar' ? col.labelAr : col.labelEn}</div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {matrix.users.map((user) => (
                <tr key={user.userId}>
                  <td className="matrix-sticky-col">
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                      <strong>{user.fullName}</strong>
                      <code className="ltr-code acl-subtitle" style={{ fontSize: 11 }}>{user.email}</code>
                      <div className="badge-wrap-list">
                        {user.isAssignedDeveloper && (
                          <StatusBadge variant="info">{c.assignedDevBadge}</StatusBadge>
                        )}
                        {user.roleNames.map((rn) => (
                          <StatusBadge key={rn} variant="neutral">{rn}</StatusBadge>
                        ))}
                      </div>
                      {canManageAcl && (
                        <div className="button-row" style={{ gap: 4 }}>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setAllUserRow(user.userId, 'GRANT')}
                          >
                            {c.grantAllRow}
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setAllUserRow(user.userId, null)}
                          >
                            {c.clearRow}
                          </Button>
                        </div>
                      )}
                    </div>
                  </td>
                  {visiblePermissions.map((col) => {
                    const explicitVal = userDraft[user.userId]?.[col.code] ?? null;
                    const preview = evaluatePreviewCell(user.userId, col.code);
                    const isOverridden = explicitVal === 'GRANT' || explicitVal === 'REVOKE';
                    return (
                      <td key={`${user.userId}-${col.code}`} className={isOverridden ? 'acl-overridden' : ''}>
                        <input
                          type="checkbox"
                          className="perm-check"
                          aria-label={`${user.fullName} — ${lang === 'ar' ? col.labelAr : col.labelEn}`}
                          disabled={!canManageAcl}
                          checked={preview.allowed}
                          title={preview.label}
                          onChange={(e) =>
                            toggleUserDocCheckbox(user.userId, col.code, e.target.checked)
                          }
                        />
                        {isOverridden && (
                          <span className="perm-key" style={{ display: 'block', fontSize: 10, marginTop: 4 }}>
                            {explicitVal === 'GRANT' ? c.srcUserGrant : c.srcUserRevoke}
                          </span>
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <CentralModalDialog
        isOpen={modal !== null}
        variant={modal?.variant ?? 'info'}
        title={modal?.title ?? ''}
        message={modal?.message ?? ''}
        confirmLabel={commonStrings.modalConfirmBtn}
        cancelLabel={modal?.variant === 'confirm' ? commonStrings.modalCancelBtn : undefined}
        closeAriaLabel={commonStrings.modalCloseAria}
        onConfirm={() => {
          const fn = modal?.onConfirmAction;
          setModal(null);
          if (fn) void fn();
        }}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}

