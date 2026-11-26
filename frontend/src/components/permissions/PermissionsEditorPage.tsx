import { DOCUMENT_GROUPS } from '../../utils/documentPermissionGroups';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import { getPermissionsCatalog, getRolePermissions, getUserPermissionsMatrix, PermissionDictionaryItem,
  PermissionsCatalogResult, replaceRolePermissions, replaceUserPermissionOverrides, UserPermissionsMatrixResult } from '../../api/permissionsApi';
import { AclDecision, getSubjectDocumentPermissions, patchSubjectDocumentPermissions, PermissionSubjectKind,
  SubjectDocumentChange, SubjectDocumentPage } from '../../api/subjectDocumentPermissionsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import { resolveBulkColumnLabel } from '../../utils/permissionLabels';
import { Button, CentralModalDialog, ErrorState, LoadingState, StandaloneFormPage } from '../ui';
import { PermissionMatrixCell, PermissionMatrixRow, PermissionMatrixTable } from '../ui/PermissionMatrixTable';

const SOURCE_LABELS: Record<string, [string, string]> = {
  ROLE_INHERITED: ['من الأدوار', 'From roles'], ROLE_GRANT: ['من الأدوار', 'From roles'],
  ROLE_REVOKE: ['منع من الدور', 'Denied by role'], ROLE_REVOKE_CONFLICT: ['منع بين الأدوار', 'Role deny takes precedence'],
  USER_GRANT: ['منح فردي', 'User grant'], USER_REVOKE: ['منع فردي', 'User deny'],
  DENIED_DEFAULT: ['غير ممنوح', 'Not granted'],
};
const cellKey = (documentId: string, code: string) => `${documentId}:${code}`;
type Decision = 'GRANT' | 'REVOKE';
interface Modal { variant: 'confirm' | 'success' | 'error'; title: string; message: string; action?: () => void }
interface GeneralData { catalog: PermissionsCatalogResult; name: string; roles: string[];
  inherited: Record<string, boolean>; initial: Record<string, Decision | null> }

export interface PermissionsEditorPageProps {
  kind: PermissionSubjectKind; subjectId: string; subjectName?: string; lang: AppLanguage;
  actorPermissions: string[]; onBack: () => void; onSessionExpired: () => void; onPermissionsChanged?: () => void;
}

export function PermissionsEditorPage({ kind, subjectId, subjectName, lang, actorPermissions,
  onBack, onSessionExpired, onPermissionsChanged }: PermissionsEditorPageProps) {
  const ar = lang === 'ar';
  const t = (arabic: string, english: string) => ar ? arabic : english;
  const [tab, setTab] = useState<'system' | 'documents'>('system');
  const [section, setSection] = useState('systems');
  const [actionPage, setActionPage] = useState(0);
  const [documentGroup, setDocumentGroup] = useState('document');
  const [search, setSearch] = useState('');
  const [docSearch, setDocSearch] = useState('');
  const [page, setPage] = useState(1);
  const [general, setGeneral] = useState<GeneralData | null>(null);
  const [draft, setDraft] = useState<Record<string, Decision | null>>({});
  const [documents, setDocuments] = useState<SubjectDocumentPage | null>(null);
  const [changes, setChanges] = useState<Record<string, SubjectDocumentChange>>({});
  const [generalLoading, setGeneralLoading] = useState(true);
  const [documentsLoading, setDocumentsLoading] = useState(false);
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [docReload, setDocReload] = useState(0);
  const [saving, setSaving] = useState(false);
  const [modal, setModal] = useState<Modal | null>(null);
  const expired = useRef(onSessionExpired);
  useEffect(() => { expired.current = onSessionExpired; }, [onSessionExpired]);
  const canManageSystem = actorPermissions.includes('permissions.manage');
  const canViewDocuments = actorPermissions.includes('documents.permissions.view');
  const canManageDocuments = actorPermissions.includes('documents.permissions.manage');

  useEffect(() => {
    let active = true;
    setGeneralLoading(true); setGeneralError(null);
    const load = async () => {
      try {
        let data: GeneralData;
        if (kind === 'roles') {
          const [catalog, role] = await Promise.all([getPermissionsCatalog(lang), getRolePermissions(subjectId, lang)]);
          const granted = new Set(role.permissionIds);
          data = { catalog, name: role.roleName, roles: [], inherited: {},
            initial: Object.fromEntries(catalog.permissions.map(p => [p.id, granted.has(p.id) ? 'GRANT' : null])) };
        } else {
          const user = await getUserPermissionsMatrix(subjectId, lang);
          data = userData(user);
        }
        if (active) { setGeneral(data); setDraft(data.initial); }
      } catch (error) {
        if (!active) return;
        if (error instanceof ApiProblemError && error.status === 401) expired.current();
        setGeneralError(error instanceof Error ? error.message : 'Unable to load permissions');
      } finally { if (active) setGeneralLoading(false); }
    };
    void load();
    return () => { active = false; };
    // Labels are bilingual in the response. A language switch must not discard edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, subjectId, reload]);

  useEffect(() => {
    if (tab !== 'documents' || !canViewDocuments) return;
    const controller = new AbortController();
    setDocumentsLoading(true); setDocumentError(null);
    const timer = setTimeout(() => {
      void getSubjectDocumentPermissions(kind, subjectId, page, docSearch, lang, controller.signal)
        .then(result => { if (!controller.signal.aborted) setDocuments(result); })
        .catch(error => {
          if (controller.signal.aborted) return;
          if (error instanceof ApiProblemError && error.status === 401) expired.current();
          setDocumentError(error instanceof Error ? error.message : 'Unable to load document permissions');
        }).finally(() => { if (!controller.signal.aborted) setDocumentsLoading(false); });
    }, 200);
    return () => { clearTimeout(timer); controller.abort(); };
    // Preserve drafts when switching display language.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, subjectId, tab, page, docSearch, docReload, canViewDocuments]);

  const systemChanges = Object.keys(draft).filter(id => draft[id] !== general?.initial[id]).length;
  const documentChanges = Object.keys(changes).length;
  const dirty = systemChanges > 0 || documentChanges > 0;
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const catalog = general?.catalog;
  const sections = useMemo(() => {
    if (!catalog) return [];
    const result = catalog.screens.filter(s => catalog.permissions.some(p => p.screenId === s.id && p.resource !== 'document'));
    return result.filter((s, index) => result.findIndex(x => x.groupId === s.groupId) === index);
  }, [catalog]);
  const activeSection = sections.some(s => s.groupId === section) ? section : sections[0]?.groupId;
  const systemPermissions = (catalog?.permissions ?? []).filter(p => p.groupId === activeSection && p.resource !== 'document');
  // A column is an action, not a collapsed list of permissions. Duplicate metadata
  // gets its own row below, so no permanent permission silently disappears on save.
  const systemColumns = (catalog?.columns ?? []).filter(c => systemPermissions.some(p => p.columnId === c.id));
  const shownColumns = systemColumns.slice(actionPage * 4, actionPage * 4 + 4);
  const globalChecked = (id: string) => draft[id] === 'GRANT' || (draft[id] == null && !!general?.inherited[id]);
  const sourceLabel = (source: string) => kind === 'roles' && source === 'ROLE_GRANT' ? t('منح صريح', 'Explicit grant')
    : kind === 'roles' && source === 'ROLE_REVOKE' ? t('منع صريح', 'Explicit deny')
      : SOURCE_LABELS[source]?.[ar ? 0 : 1] ?? t('إعداد الدور', 'Role setting');
  const generalCell = (permission: PermissionDictionaryItem): PermissionMatrixCell => {
    const decision = draft[permission.id];
    return { id: permission.id, checked: globalChecked(permission.id), explicit: kind === 'users' && decision != null,
      source: kind === 'users' ? sourceLabel(decision === 'GRANT' ? 'USER_GRANT' : decision === 'REVOKE' ? 'USER_REVOKE'
        : general?.inherited[permission.id] ? 'ROLE_INHERITED' : 'DENIED_DEFAULT') : undefined };
  };
  const systemRows: PermissionMatrixRow[] = [];
  for (const screen of catalog?.screens ?? []) {
    if (screen.groupId !== activeSection || !`${screen.titleAr} ${screen.titleEn}`.toLowerCase().includes(search.toLowerCase().trim())) continue;
    const permissions = systemPermissions.filter(p => p.screenId === screen.id);
    const cells: Record<string, PermissionMatrixCell> = {};
    for (const permission of permissions) {
      if (!shownColumns.some(c => c.id === permission.columnId)) continue;
      if (cells[permission.columnId]) {
        systemRows.push({ id: permission.id, title: `${ar ? screen.titleAr : screen.titleEn} (${t('إضافية', 'additional')})`,
          cells: { [permission.columnId]: generalCell(permission) } });
      } else cells[permission.columnId] = generalCell(permission);
    }
    if (Object.keys(cells).length) systemRows.push({ id: screen.id, title: ar ? screen.titleAr : screen.titleEn, cells });
  }
  const group = DOCUMENT_GROUPS.find(g => g.id === documentGroup)!;
  const docColumns = (documents?.permissions ?? []).filter(p => group.codes.includes(p.code));
  const documentRows: PermissionMatrixRow[] = (documents?.items ?? []).map(row => ({
    id: row.documentId, title: row.title, subtitle: row.groupName,
    cells: Object.fromEntries(docColumns.map(permission => {
      const cell = row.cells[permission.code]; const id = cellKey(row.documentId, permission.code);
      const decision = changes[id] ? changes[id].decision : cell?.decision;
      return [permission.code, { id, checked: decision === 'GRANT' || (decision == null && !!cell?.defaultAllowed),
        explicit: decision != null, disabled: !row.canManage,
        source: sourceLabel(changes[id] ? decision === null ? (cell.defaultAllowed ? 'ROLE_INHERITED' : 'DENIED_DEFAULT')
          : `${kind === 'users' ? 'USER' : 'ROLE'}_${decision}` : cell?.source ?? 'DENIED_DEFAULT') }];
    })),
  }));

  function updateDocuments(ids: string[], checked: boolean | null) {
    setChanges(previous => {
      const next = { ...previous };
      for (const id of ids) {
        const [documentId, permissionCode] = id.split(':');
        const row = documents?.items.find(r => r.documentId === documentId);
        const cell = row?.cells[permissionCode];
        if (!row?.canManage || !cell || !canManageDocuments) continue;
        const decision: AclDecision = checked === null ? null : checked ? 'GRANT' : 'REVOKE';
        const expectedDecision = previous[id] ? previous[id].expectedDecision : cell.decision;
        if (decision === expectedDecision) delete next[id];
        else next[id] = { documentId, permissionCode, decision, expectedDecision };
      }
      return next;
    });
  }
  function leave() {
    if (!dirty) { onBack(); return; }
    setModal({ variant: 'confirm', title: t('تغييرات غير محفوظة', 'Unsaved changes'),
      message: t('هل تريد المغادرة وتجاهل التغييرات في التبويبين؟', 'Leave and discard changes in both tabs?'), action: onBack });
  }
  async function save() {
    if (saving) return;
    setModal(null); setSaving(true);
    try {
      if (tab === 'system' && general && canManageSystem) {
        if (kind === 'roles') {
          const result = await replaceRolePermissions(subjectId, general.catalog.permissions.filter(p => draft[p.id] === 'GRANT').map(p => p.id), lang);
          const granted = new Set(result.permissionIds);
          const initial = Object.fromEntries(general.catalog.permissions.map(p => [p.id, granted.has(p.id) ? 'GRANT' as const : null]));
          setGeneral({ ...general, initial }); setDraft(initial);
        } else {
          const overrides = Object.entries(draft).filter((pair): pair is [string, Decision] => pair[1] != null)
            .map(([permissionId, decision]) => ({ permissionId, decision }));
          const result = userData(await replaceUserPermissionOverrides(subjectId, overrides, lang));
          setGeneral(result); setDraft(result.initial);
        }
      } else if (tab === 'documents' && canManageDocuments) {
        await patchSubjectDocumentPermissions(kind, subjectId, Object.values(changes), lang);
        setChanges({}); setDocReload(x => x + 1);
      } else return;
      onPermissionsChanged?.();
      setModal({ variant: 'success', title: t('تم الحفظ', 'Saved'),
        message: t('تم حفظ تغييرات التبويب الحالي.', 'Changes in the current tab have been saved.') });
    } catch (error) {
      if (error instanceof ApiProblemError && error.status === 401) onSessionExpired();
      setModal({ variant: 'error', title: t('تعذر حفظ الصلاحيات', 'Unable to save permissions'),
        message: error instanceof Error ? error.message : t('حاول مرة أخرى.', 'Please retry.') });
    } finally { setSaving(false); }
  }
  const canEdit = tab === 'system' ? canManageSystem && !generalLoading && !generalError : canManageDocuments && !documentsLoading && !documentError && canViewDocuments;
  const currentChanges = tab === 'system' ? systemChanges : documentChanges;
  const tableLabels = { rowAll: t('تحديد الصف المعروض', 'Select visible row'), columnAll: t('تحديد العمود في النتائج المعروضة', 'Select column on this page'),
    reset: kind === 'users' ? t('حسب الأدوار', 'Use inherited') : t('إلغاء القرار الصريح', 'Clear explicit decision'), empty: t('لا توجد نتائج مطابقة.', 'No matching results.') };
  return <>
    <StandaloneFormPage formTitle={`${kind === 'roles' ? t('صلاحيات الدور', 'Role permissions') : t('صلاحيات المستخدم', 'User permissions')}: ${general?.name ?? subjectName ?? '…'}`}
      backButtonLabel={t('رجوع', 'Back')} cancelButtonLabel={t('إلغاء', 'Cancel')} submitButtonLabel={t('حفظ', 'Save')}
      isSubmitting={saving} submitDisabled={!canEdit || !currentChanges} onBack={leave} onCancel={leave} onSubmit={() => {
        if (!canEdit || !currentChanges) return;
        setModal({ variant: 'confirm', title: t('حفظ الصلاحيات', 'Save permissions'),
          message: t('سيتم حفظ تغييرات التبويب الحالي وتطبيقها على الصلاحيات. هل تؤكد؟', 'Save the current tab and apply these permission changes?'), action: () => void save() });
      }}>
      <div className="permissions-editor" aria-busy={saving}>
        {general?.roles.length ? <p className="permissions-role-summary">{t('الأدوار المسندة: ', 'Assigned roles: ')}{general.roles.join('، ')}</p> : null}
        <div className="permissions-tabs" role="tablist" aria-label={t('نطاق الصلاحيات', 'Permission scope')}>
          {(['system', 'documents'] as const).map(value => <button type="button" key={value} id={`permissions-${value}-tab`}
            role="tab" aria-selected={tab === value} aria-controls={`permissions-${value}-panel`} disabled={saving}
            onClick={() => setTab(value)}>{value === 'system' ? t('صلاحيات النظام', 'System permissions') : t('صلاحيات الوثائق', 'Document permissions')}
            {(value === 'system' ? systemChanges : documentChanges) > 0 && <span className="permissions-change-dot" aria-label={t('تغييرات غير محفوظة', 'Unsaved changes')}> •</span>}</button>)}
        </div>
        <div role="tabpanel" id="permissions-system-panel" aria-labelledby="permissions-system-tab" hidden={tab !== 'system'}>
          {generalLoading ? <LoadingState title={t('جارٍ تحميل الصلاحيات…', 'Loading permissions…')} /> : generalError ? <ErrorState
            title={t('تعذر تحميل الصلاحيات', 'Unable to load permissions')} message={generalError} retryLabel={t('إعادة المحاولة', 'Retry')} onRetry={() => setReload(x => x + 1)} /> : <>
            <div className="permissions-groups" aria-label={t('أقسام النظام', 'System sections')}>{sections.map(s => <button type="button" key={s.groupId}
              aria-pressed={activeSection === s.groupId} disabled={saving} onClick={() => { setSection(s.groupId); setActionPage(0); setSearch(''); }}>{ar ? s.groupLabelAr : s.groupLabelEn}</button>)}</div>
            <div className="permissions-toolbar"><input className="text-input" type="search" value={search} onChange={e => setSearch(e.target.value)}
              placeholder={t('ابحث عن شاشة…', 'Search screens…')} aria-label={t('بحث في الشاشات', 'Search screens')} />
              {systemColumns.length > 4 && <div className="permissions-groups">{Array.from({ length: Math.ceil(systemColumns.length / 4) }, (_, i) => <button type="button" key={i}
                aria-pressed={actionPage === i} onClick={() => setActionPage(i)}>{i === 0 ? t('الإجراءات الأساسية', 'Basic actions') : `${t('إجراءات إضافية', 'Additional actions')} ${i}`}</button>)}</div>}</div>
            <PermissionMatrixTable rows={systemRows} columns={shownColumns.map(c => ({ id: c.id, label: resolveBulkColumnLabel(c.id, ar ? c.labelAr : c.labelEn, lang) }))}
              rowLabel={t('الشاشة / الخدمة', 'Screen / service')} disabled={!canManageSystem || saving} labels={tableLabels}
              onChange={(ids, checked) => setDraft(previous => { const next = { ...previous }; for (const id of ids) next[id] = kind === 'roles'
                ? checked ? 'GRANT' : null : checked ? general?.inherited[id] ? null : 'GRANT' : general?.inherited[id] ? 'REVOKE' : null; return next; })}
              onReset={kind === 'users' ? id => setDraft(previous => ({ ...previous, [id]: null })) : undefined} />
          </>}
        </div>
        <div role="tabpanel" id="permissions-documents-panel" aria-labelledby="permissions-documents-tab" hidden={tab !== 'documents'}>
          {!canViewDocuments ? <p className="permissions-notice">{t('لا تملك صلاحية عرض أذونات الوثائق.', 'You do not have permission to view document access.')}</p> : <>
            <div className="permissions-groups" aria-label={t('مجموعات إجراءات الوثائق', 'Document action groups')}>{DOCUMENT_GROUPS.map(g => <button type="button" key={g.id}
              aria-pressed={g.id === documentGroup} disabled={saving} onClick={() => setDocumentGroup(g.id)}>{ar ? g.ar : g.en}</button>)}</div>
            <div className="permissions-toolbar"><input className="text-input" type="search" value={docSearch} disabled={saving} onChange={e => { setDocSearch(e.target.value); setPage(1); }}
              placeholder={t('ابحث عن وثيقة…', 'Search documents…')} aria-label={t('بحث في الوثائق', 'Search documents')} />
              <span>{documents?.totalCount ?? 0} {t('وثيقة', 'documents')}</span></div>
            <p className="permissions-notice">{t('تظهر الوثائق التي تملك إدارة الوصول إليها. تنفيذ أي إجراء يتطلب أيضًا صلاحيته العامة وقواعد العمل.', 'Only documents whose access you can administer are listed. Actions also require their system permission and business rules.')}</p>
            {documentsLoading ? <LoadingState title={t('جارٍ تحميل الوثائق…', 'Loading documents…')} /> : documentError ? <ErrorState title={t('تعذر تحميل الوثائق', 'Unable to load documents')}
              message={documentError} retryLabel={t('إعادة المحاولة', 'Retry')} onRetry={() => setDocReload(x => x + 1)} /> : <PermissionMatrixTable rows={documentRows}
              columns={docColumns.map(p => ({ id: p.code, label: ar ? p.labelAr : p.labelEn }))} rowLabel={t('الوثيقة / مجموعة API', 'Document / API group')}
              disabled={!canManageDocuments || saving} labels={tableLabels} onChange={updateDocuments} onReset={id => updateDocuments([id], null)} />}
            {documents && documents.totalPages > 1 && <div className="permissions-pagination">
              <Button type="button" variant="outline" disabled={page <= 1 || documentsLoading || saving} onClick={() => setPage(p => p - 1)}>{t('السابق', 'Previous')}</Button>
              <span>{documents.page} / {documents.totalPages}</span>
              <Button type="button" variant="outline" disabled={page >= documents.totalPages || documentsLoading || saving} onClick={() => setPage(p => p + 1)}>{t('التالي', 'Next')}</Button>
            </div>}
          </>}
        </div>
        <p className="permissions-save-status" role="status">{currentChanges ? `${currentChanges} ${t('تغيير غير محفوظ في هذا التبويب', 'unsaved changes in this tab')}` : t('لا توجد تغييرات في هذا التبويب', 'No changes in this tab')}
          {!canEdit && !generalLoading && !documentsLoading ? ` · ${t('للقراءة فقط', 'Read only')}` : ''}</p>
      </div>
    </StandaloneFormPage>
    <CentralModalDialog isOpen={modal !== null} variant={modal?.variant ?? 'info'} title={modal?.title ?? ''} message={modal?.message ?? ''}
      confirmLabel={UI_STRINGS[lang].modalConfirmBtn} cancelLabel={modal?.variant === 'confirm' ? UI_STRINGS[lang].modalCancelBtn : undefined}
      closeAriaLabel={UI_STRINGS[lang].modalCloseAria} onConfirm={() => { const action = modal?.action; setModal(null); action?.(); }}
      onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined} />
  </>;
}

function userData(user: UserPermissionsMatrixResult): GeneralData {
  return { name: user.fullName, roles: user.activeRoles.map(r => r.name),
    catalog: { ...user, permissions: user.permissions.map(p => ({ ...p, id: p.permissionId })) },
    inherited: Object.fromEntries(user.permissions.map(p => [p.permissionId, p.isInherited])),
    initial: Object.fromEntries(user.permissions.map(p => [p.permissionId, p.overrideDecision ?? null])) };
}
