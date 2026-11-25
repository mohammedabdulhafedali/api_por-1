import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  FileRequirementTemplateItem,
  RequirementFileTypeOption,
  RequirementRuleInput,
  RequirementRuleMode,
  RequirementTemplateSnapshotItem,
  createRequirementTemplate,
  listRequirementFileTypeOptions,
  listRequirementTemplateSnapshots,
  listRequirementTemplates,
  updateRequirementTemplate,
} from '../../api/governanceConfigApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  ErrorState,
  FormField,
  PaginationBar,
  SearchFilterBar,
  SelectInput,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  TextareaInput,
} from '../ui';

interface RequirementTemplatesPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type PageMode = 'list' | 'create' | 'edit' | 'snapshots';

interface RuleDraftRow {
  key: string;
  fileTypeId: string;
  requirementMode: RequirementRuleMode;
  minCount: string;
  maxCount: string;
  conditionSpecJson: string;
}

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    pageTitle: 'قوالب متطلبات الملفات واللقطات التاريخية (UI-23)',
    pageDesc:
      'تحديد إلزامية الملفات والحد الأدنى والأقصى لكل نوع ملف ضمن قوالب مع حفظ لقطة تاريخية غير قابلة للتعديل عند كل إنشاء أو تحديث.',
    noViewTitle: 'صلاحية عرض قوالب المتطلبات غير متوفرة',
    noViewDesc: 'يتطلب عرض قوالب متطلبات الملفات توفر صلاحية requirements.view.',
    addBtn: 'إضافة قالب متطلبات',
    searchPlaceholder: 'ابحث باسم القالب...',
    searchAria: 'البحث في قوالب متطلبات الملفات',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'اسم القالب',
    colRules: 'قواعد الملفات',
    colSnapshots: 'اللقطات التاريخية',
    colDocs: 'الوثائق المرتبطة',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    modeRequired: 'إلزامي',
    modeOptional: 'اختياري',
    modeConditional: 'مشروط',
    snapshotsBtn: 'اللقطات',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createBadge: 'إضافة قالب متطلبات · UI-23 / M05',
    editBadge: 'تعديل قالب متطلبات · UI-23 / M05',
    snapshotsBadge: 'اللقطات التاريخية للقالب · M05',
    createTitle: 'إضافة قالب متطلبات ملفات جديد',
    editTitle: 'تعديل قالب متطلبات الملفات',
    snapshotsTitle: 'السجل التاريخي للقطات القالب',
    formSub: 'يتم حفظ لقطة تاريخية تلقائيًا عند الإنشاء وكل تحديث قبل ارتباط القالب بوثائق API نشطة.',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    nameLabel: 'اسم القالب',
    rulesSectionTitle: 'قواعد أنواع الملفات في القالب',
    addRuleBtn: 'إضافة نوع ملف',
    removeRuleBtn: 'إزالة',
    fileTypeLabel: 'نوع الملف',
    selectFileType: '-- اختر نوع الملف --',
    modeLabel: 'وضع الإلزامية',
    minLabel: 'الحد الأدنى',
    maxLabel: 'الحد الأقصى',
    conditionLabel: 'شرط JSON (للوضع المشروط فقط)',
    noActiveFileTypesNotice: 'لا توجد أنواع ملفات نشطة بعد؛ عرّف أنواع الملفات أولًا من UI-04.',
    validationName: 'اسم القالب مطلوب ويجب ألا يقل عن حرفين.',
    validationRulesEmpty: 'يجب إضافة قاعدة نوع ملف واحدة على الأقل داخل القالب.',
    validationRuleFields: 'تأكد من اختيار نوع ملف صالح وعدم تكراره، وصحة الحدين الأدنى والأقصى.',
    createdTitle: 'تم إنشاء قالب المتطلبات',
    createdMsg: 'تم حفظ قالب متطلبات الملفات وحفظ اللقطة التاريخية الأولى بنجاح.',
    updatedTitle: 'تم تحديث القالب وحفظ اللقطة',
    updatedMsg: 'تم حفظ تعديلات القالب وإضافة لقطة تاريخية جديدة في سجل التدقيق.',
    confirmDeactivateTitle: 'تأكيد تعطيل قالب المتطلبات',
    confirmDeactivateMsg: 'هل تريد تعطيل القالب المحدد؟ تمنع حماية المراجع التعطيل إذا كان مرتبطًا بوثائق نشطة.',
    snapshotsEmpty: 'لا توجد لقطات مسجلة لهذا القالب.',
    snapshotVersion: 'النسخة',
    snapshotDate: 'تاريخ الالتقاط',
    snapshotActor: 'بواسطة',
    snapshotOp: 'العملية',
    snapshotCountLabel: 'لقطات',
    inUseLockedHint: 'مرتبط بوثائق نشطة (التعديل البنيوي مقفول لحماية المراجع)',
    loadingText: 'جارٍ تحميل البيانات...',
    pageWord: 'صفحة',
    ofWord: 'من',
  },
  en: {
    pageTitle: 'File Requirement Templates & Snapshots (UI-23)',
    pageDesc:
      'Configure file modes and min/max counts per template with immutable historical snapshots recorded on every creation or update.',
    noViewTitle: 'Missing requirement templates permission',
    noViewDesc: 'Viewing requirement templates requires requirements.view permission.',
    addBtn: 'Add Requirement Template',
    searchPlaceholder: 'Search by template name...',
    searchAria: 'Search requirement templates',
    filterAria: 'Filter by status',
    filterAll: 'All statuses',
    filterActive: 'Active only',
    filterInactive: 'Inactive only',
    colName: 'Template Name',
    colRules: 'File Rules',
    colSnapshots: 'Historical Snapshots',
    colDocs: 'Linked Docs',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    modeRequired: 'Required',
    modeOptional: 'Optional',
    modeConditional: 'Conditional',
    snapshotsBtn: 'Snapshots',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createBadge: 'Create Template · UI-23 / M05',
    editBadge: 'Edit Template · UI-23 / M05',
    snapshotsBadge: 'Historical Snapshots · M05',
    createTitle: 'Create File Requirement Template',
    editTitle: 'Edit File Requirement Template',
    snapshotsTitle: 'Template Historical Snapshots',
    formSub: 'An immutable snapshot is stored automatically upon creation and each pre-use update.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    nameLabel: 'Template Name',
    rulesSectionTitle: 'Template File Type Rules',
    addRuleBtn: 'Add File Type Rule',
    removeRuleBtn: 'Remove',
    fileTypeLabel: 'File Type',
    selectFileType: '-- Select File Type --',
    modeLabel: 'Requirement Mode',
    minLabel: 'Min Count',
    maxLabel: 'Max Count',
    conditionLabel: 'Condition JSON (Conditional mode)',
    noActiveFileTypesNotice: 'No active file types found; define file types first in UI-04.',
    validationName: 'Template name is required (at least 2 characters).',
    validationRulesEmpty: 'At least one file requirement rule is required.',
    validationRuleFields: 'Select unique valid file types with valid min/max counts.',
    createdTitle: 'Requirement Template Created',
    createdMsg: 'Template created and initial historical snapshot recorded successfully.',
    updatedTitle: 'Template Updated & Snapshotted',
    updatedMsg: 'Template updated and a new immutable snapshot recorded in audit history.',
    confirmDeactivateTitle: 'Confirm Template Deactivation',
    confirmDeactivateMsg: 'Deactivate the selected template? Active API document references are protected.',
    snapshotsEmpty: 'No historical snapshots found for this template.',
    snapshotVersion: 'Version',
    snapshotDate: 'Captured At',
    snapshotActor: 'Actor',
    snapshotOp: 'Operation',
    snapshotCountLabel: 'snapshots',
    inUseLockedHint: 'Linked to active documents (structural edits locked by reference protection)',
    loadingText: 'Loading data...',
    pageWord: 'Page',
    ofWord: 'of',
  },
} as const;

export function RequirementTemplatesPage({
  lang,
  permissions,
  onSessionExpired,
}: RequirementTemplatesPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canView = permSet.has('requirements.view');
  const canManage = permSet.has('requirements.manage');

  const [mode, setMode] = useState<PageMode>('list');
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<FileRequirementTemplateItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [modal, setModal] = useState<ModalState | null>(null);

  const [fileTypeOptions, setFileTypeOptions] = useState<RequirementFileTypeOption[]>([]);
  const [editingTemplate, setEditingTemplate] = useState<FileRequirementTemplateItem | null>(null);
  const [templateName, setTemplateName] = useState('');
  const [ruleRows, setRuleRows] = useState<RuleDraftRow[]>([]);
  const [nameError, setNameError] = useState<string | undefined>();
  const [rulesError, setRulesError] = useState<string | undefined>();
  const nameInputRef = useRef<HTMLInputElement | null>(null);

  const [snapshotsTemplate, setSnapshotsTemplate] = useState<FileRequirementTemplateItem | null>(null);
  const [snapshots, setSnapshots] = useState<RequirementTemplateSnapshotItem[]>([]);
  const [snapshotsLoading, setSnapshotsLoading] = useState(false);

  const handleApiError = useCallback(
    (err: unknown, fallbackTitle: string) => {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const message = err instanceof Error ? err.message : fallbackTitle;
      setModal({ variant: 'error', title: fallbackTitle, message });
    },
    [onSessionExpired]
  );

  const loadTemplates = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const isActive = statusFilter === 'active' ? true : statusFilter === 'inactive' ? false : null;
      const result = await listRequirementTemplates(
        { page, pageSize: 10, search: searchQuery, isActive },
        lang
      );
      setItems(result.items);
      setTotalCount(result.totalCount);
      setTotalPages(Math.max(1, result.totalPages));
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg(err instanceof Error ? err.message : 'Load error');
    } finally {
      setLoading(false);
    }
  }, [canView, lang, onSessionExpired, page, searchQuery, statusFilter]);

  const loadFileTypeOptions = useCallback(async () => {
    if (!canView) return;
    try {
      const opts = await listRequirementFileTypeOptions(lang);
      setFileTypeOptions(opts);
    } catch {
      // Ignore secondary lookup failure until form submission
    }
  }, [canView, lang]);

  useEffect(() => {
    void loadTemplates();
  }, [loadTemplates]);

  useEffect(() => {
    void loadFileTypeOptions();
  }, [loadFileTypeOptions]);

  const makeBlankRule = useCallback(
    (index: number): RuleDraftRow => ({
      key: `rule-${Date.now()}-${index}`,
      fileTypeId: fileTypeOptions[index]?.id ?? fileTypeOptions[0]?.id ?? '',
      requirementMode: 'REQUIRED',
      minCount: '1',
      maxCount: '1',
      conditionSpecJson: '',
    }),
    [fileTypeOptions]
  );

  const openCreate = () => {
    setEditingTemplate(null);
    setTemplateName('');
    setRuleRows([makeBlankRule(0)]);
    setNameError(undefined);
    setRulesError(undefined);
    setMode('create');
  };

  const openEdit = (tpl: FileRequirementTemplateItem) => {
    setEditingTemplate(tpl);
    setTemplateName(tpl.name);
    setRuleRows(
      tpl.rules.map((r, idx) => ({
        key: `rule-edit-${idx}-${r.fileTypeId}`,
        fileTypeId: r.fileTypeId,
        requirementMode: (r.requirementMode.toUpperCase() as RequirementRuleMode) || 'REQUIRED',
        minCount: String(r.minCount),
        maxCount: String(r.maxCount),
        conditionSpecJson: r.conditionSpecJson ?? '',
      }))
    );
    setNameError(undefined);
    setRulesError(undefined);
    setMode('edit');
  };

  const openSnapshots = async (tpl: FileRequirementTemplateItem) => {
    setSnapshotsTemplate(tpl);
    setSnapshots([]);
    setMode('snapshots');
    setSnapshotsLoading(true);
    try {
      const list = await listRequirementTemplateSnapshots(tpl.id, lang);
      setSnapshots(list);
    } catch (err) {
      handleApiError(err, c.snapshotsTitle);
    } finally {
      setSnapshotsLoading(false);
    }
  };

  const addRuleRow = () => {
    setRuleRows((prev) => [...prev, makeBlankRule(prev.length)]);
  };

  const updateRuleRow = (key: string, patch: Partial<RuleDraftRow>) => {
    setRuleRows((prev) =>
      prev.map((row) => {
        if (row.key !== key) return row;
        const next = { ...row, ...patch };
        if (patch.requirementMode === 'OPTIONAL') {
          next.minCount = '0';
          if (Number(next.maxCount) < 1) next.maxCount = '1';
        } else if (patch.requirementMode === 'REQUIRED' && Number(next.minCount) < 1) {
          next.minCount = '1';
          if (Number(next.maxCount) < 1) next.maxCount = '1';
        }
        return next;
      })
    );
  };

  const removeRuleRow = (key: string) => {
    setRuleRows((prev) => prev.filter((r) => r.key !== key));
  };

  const handleSubmitForm = async () => {
    setNameError(undefined);
    setRulesError(undefined);

    const trimmedName = templateName.trim();
    if (trimmedName.length < 2) {
      setNameError(c.validationName);
      setModal({
        variant: 'error',
        title: c.validationName,
        message: c.validationName,
      });
      return;
    }

    if (ruleRows.length === 0) {
      setRulesError(c.validationRulesEmpty);
      return;
    }

    const parsedRules: RequirementRuleInput[] = [];
    const seenTypes = new Set<string>();
    for (const row of ruleRows) {
      const min = Number(row.minCount);
      const max = Number(row.maxCount);
      if (
        !row.fileTypeId ||
        seenTypes.has(row.fileTypeId) ||
        !Number.isInteger(min) ||
        !Number.isInteger(max) ||
        min < 0 ||
        max < min ||
        max < 1 ||
        (row.requirementMode === 'REQUIRED' && min < 1) ||
        (row.requirementMode === 'OPTIONAL' && min !== 0)
      ) {
        setRulesError(c.validationRuleFields);
        return;
      }
      seenTypes.add(row.fileTypeId);
      parsedRules.push({
        fileTypeId: row.fileTypeId,
        requirementMode: row.requirementMode,
        minCount: min,
        maxCount: max,
        conditionSpecJson:
          row.requirementMode === 'CONDITIONAL' && row.conditionSpecJson.trim()
            ? row.conditionSpecJson.trim()
            : null,
      });
    }

    setSubmitting(true);
    try {
      if (mode === 'create') {
        await createRequirementTemplate({ name: trimmedName, rules: parsedRules }, lang);
        setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
      } else if (editingTemplate) {
        await updateRequirementTemplate(
          editingTemplate.id,
          { name: trimmedName, rules: parsedRules },
          lang
        );
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
      }
      setMode('list');
      await loadTemplates();
    } catch (err) {
      handleApiError(err, mode === 'create' ? c.createTitle : c.editTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleStatus = (tpl: FileRequirementTemplateItem) => {
    const runToggle = async () => {
      setSubmitting(true);
      try {
        await updateRequirementTemplate(tpl.id, { isActive: !tpl.isActive }, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        await loadTemplates();
      } catch (err) {
        handleApiError(err, c.editTitle);
      } finally {
        setSubmitting(false);
      }
    };

    if (tpl.isActive) {
      setModal({
        variant: 'confirm',
        title: c.confirmDeactivateTitle,
        message: c.confirmDeactivateMsg,
        onConfirmAction: runToggle,
      });
    } else {
      void runToggle();
    }
  };

  const formatModeLabel = (rawMode: string) => {
    switch (rawMode.toUpperCase()) {
      case 'REQUIRED':
        return c.modeRequired;
      case 'OPTIONAL':
        return c.modeOptional;
      default:
        return c.modeConditional;
    }
  };

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noViewTitle} message={c.noViewDesc} />
      </section>
    );
  }

  return (
    <>
      {mode === 'snapshots' && snapshotsTemplate ? (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <span className="mode-pill">{c.snapshotsBadge}</span>
              <h2 className="card-title heading-font" style={{ marginTop: 8 }}>
                {c.snapshotsTitle}: {snapshotsTemplate.name}
              </h2>
            </div>
            <Button variant="outline" onClick={() => setMode('list')}>
              {c.backBtn}
            </Button>
          </div>

          {snapshotsLoading ? (
            <p className="card-subtitle">{c.loadingText}</p>
          ) : snapshots.length === 0 ? (
            <p className="card-subtitle">{c.snapshotsEmpty}</p>
          ) : (
            <div className="table-scroll-container">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{c.snapshotVersion}</th>
                    <th>{c.snapshotOp}</th>
                    <th>{c.snapshotDate}</th>
                    <th>{c.snapshotActor}</th>
                    <th>{c.colRules}</th>
                  </tr>
                </thead>
                <tbody>
                  {snapshots.map((snap) => (
                    <tr key={snap.auditEventId}>
                      <td>
                        <StatusBadge variant="info">{`v${snap.versionNumber}`}</StatusBadge>
                      </td>
                      <td>
                        <code className="ltr-code">{snap.operation}</code>
                      </td>
                      <td>
                        <code className="ltr-code">
                          {new Date(snap.capturedAtUtc).toISOString()}
                        </code>
                      </td>
                      <td>{snap.actorName}</td>
                      <td>
                        <div className="badge-wrap-list">
                          {snap.rules.map((r) => (
                            <StatusBadge
                              key={`${snap.auditEventId}-${r.fileTypeId}`}
                              variant="neutral"
                            >
                              {`${r.fileTypeName}: ${formatModeLabel(r.requirementMode)} (${r.minCount}–${r.maxCount})`}
                            </StatusBadge>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : mode === 'create' || mode === 'edit' ? (
        <StandaloneFormPage
          modeBadgeLabel={mode === 'create' ? c.createBadge : c.editBadge}
          formTitle={mode === 'create' ? c.createTitle : c.editTitle}
          formSubtitle={c.formSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={mode === 'create' ? c.saveCreateBtn : c.saveEditBtn}
          cancelButtonLabel={c.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSubmitForm()}
        >
          {fileTypeOptions.length === 0 && (
            <p className="form-error-text">{c.noActiveFileTypesNotice}</p>
          )}
          <FormField
            id="req-template-name"
            label={c.nameLabel}
            required
            error={nameError}
          >
            <TextInput
              ref={nameInputRef}
              id="req-template-name"
              value={templateName}
              hasError={Boolean(nameError)}
              onChange={(e) => setTemplateName(e.target.value)}
            />
          </FormField>

          <div style={{ marginTop: 18 }}>
            <div className="card-header-row" style={{ marginBottom: 10 }}>
              <h3 className="card-title heading-font">{c.rulesSectionTitle}</h3>
              <Button variant="outline" size="sm" onClick={addRuleRow}>
                {c.addRuleBtn}
              </Button>
            </div>

            {rulesError && (
              <p className="form-error-text" role="alert">
                {rulesError}
              </p>
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {ruleRows.map((row, idx) => (
                <div key={row.key} className="surface-card" style={{ padding: 14 }}>
                  <div className="form-grid-2">
                    <FormField
                      id={`rule-ft-${idx}`}
                      label={c.fileTypeLabel}
                      required
                    >
                      <SelectInput
                        id={`rule-ft-${idx}`}
                        value={row.fileTypeId}
                        onChange={(e) =>
                          updateRuleRow(row.key, { fileTypeId: e.target.value })
                        }
                        options={[
                          { value: '', label: c.selectFileType },
                          ...fileTypeOptions.map((ft) => ({
                            value: ft.id,
                            label: `${ft.displayName} (${ft.extensions.join(', ')})`,
                          })),
                        ]}
                      />
                    </FormField>

                    <FormField id={`rule-mode-${idx}`} label={c.modeLabel} required>
                      <SelectInput
                        id={`rule-mode-${idx}`}
                        value={row.requirementMode}
                        onChange={(e) =>
                          updateRuleRow(row.key, {
                            requirementMode: e.target.value as RequirementRuleMode,
                          })
                        }
                        options={[
                          { value: 'REQUIRED', label: c.modeRequired },
                          { value: 'OPTIONAL', label: c.modeOptional },
                          { value: 'CONDITIONAL', label: c.modeConditional },
                        ]}
                      />
                    </FormField>

                    <FormField id={`rule-min-${idx}`} label={c.minLabel} required>
                      <TextInput
                        id={`rule-min-${idx}`}
                        ltrCode
                        type="number"
                        min={0}
                        max={100}
                        value={row.minCount}
                        onChange={(e) =>
                          updateRuleRow(row.key, { minCount: e.target.value })
                        }
                      />
                    </FormField>

                    <FormField id={`rule-max-${idx}`} label={c.maxLabel} required>
                      <TextInput
                        id={`rule-max-${idx}`}
                        ltrCode
                        type="number"
                        min={1}
                        max={100}
                        value={row.maxCount}
                        onChange={(e) =>
                          updateRuleRow(row.key, { maxCount: e.target.value })
                        }
                      />
                    </FormField>
                  </div>

                  {row.requirementMode === 'CONDITIONAL' && (
                    <FormField id={`rule-cond-${idx}`} label={c.conditionLabel}>
                      <TextareaInput
                        id={`rule-cond-${idx}`}
                        rows={2}
                        value={row.conditionSpecJson}
                        placeholder='{"whenExtension": ".pdf"}'
                        onChange={(e) =>
                          updateRuleRow(row.key, { conditionSpecJson: e.target.value })
                        }
                      />
                    </FormField>
                  )}

                  {ruleRows.length > 1 && (
                    <div className="button-row" style={{ marginTop: 8 }}>
                      <Button
                        variant="danger"
                        size="sm"
                        onClick={() => removeRuleRow(row.key)}
                      >
                        {c.removeRuleBtn}
                      </Button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        </StandaloneFormPage>
      ) : (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">{c.pageTitle}</h2>
              <p className="card-subtitle">{c.pageDesc}</p>
            </div>
            {canManage && (
              <Button variant="primary" disabled={submitting} onClick={openCreate}>
                {c.addBtn}
              </Button>
            )}
          </div>

          <SearchFilterBar
            searchValue={searchQuery}
            searchPlaceholder={c.searchPlaceholder}
            searchAriaLabel={c.searchAria}
            onSearchChange={(val) => {
              setSearchQuery(val);
              setPage(1);
            }}
            filterValue={statusFilter}
            filterAriaLabel={c.filterAria}
            filterPlaceholder={c.filterAll}
            filterOptions={[
              { value: 'active', label: c.filterActive },
              { value: 'inactive', label: c.filterInactive },
            ]}
            onFilterChange={(val) => {
              setStatusFilter(val);
              setPage(1);
            }}
          />

          {errorMsg ? (
            <ErrorState
              title={commonStrings.tableErrorTitle}
              message={errorMsg}
              retryLabel={commonStrings.tableRetryBtn}
              onRetry={() => void loadTemplates()}
            />
          ) : loading ? (
            <div className="state-box" role="status" aria-live="polite">
              <div className="state-box-title">{c.loadingText}</div>
            </div>
          ) : items.length === 0 ? (
            <div className="empty-state-box empty" role="status">
              <div className="state-box-title">{commonStrings.tableEmptyTitle}</div>
              <p className="state-box-desc">{commonStrings.tableEmptyDesc}</p>
            </div>
          ) : (
            <>
              <div className="grid-2" style={{ marginTop: 14 }}>
                {items.map((row) => (
                  <div key={row.id} className="panel">
                    <div className="panel-head">
                      <div>
                        <div className="row wrap">
                          <div className="panel-title">
                            <strong>{row.name}</strong>
                          </div>
                          <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
                            {row.isActive ? c.activeBadge : c.inactiveBadge}
                          </StatusBadge>
                          <span className="tag">
                            {row.snapshotCount} {c.snapshotCountLabel}
                          </span>
                          <span className="tag ltr-code">{row.documentsCount}</span>
                        </div>
                        <div className="panel-desc">{c.colRules}</div>
                      </div>
                      <div className="button-row">
                        <Button variant="outline" size="sm" onClick={() => void openSnapshots(row)}>
                          {c.snapshotsBtn}
                        </Button>
                        {canManage && (
                          <>
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={row.documentsCount > 0}
                              title={row.documentsCount > 0 ? c.inUseLockedHint : undefined}
                              onClick={() => openEdit(row)}
                            >
                              {c.editBtn}
                            </Button>
                            <Button
                              variant={row.isActive ? 'danger' : 'secondary'}
                              size="sm"
                              onClick={() => handleToggleStatus(row)}
                            >
                              {row.isActive ? c.deactivateBtn : c.activateBtn}
                            </Button>
                          </>
                        )}
                      </div>
                    </div>
                    <div className="stack">
                      {row.rules.map((r) => (
                        <div key={r.id} className="item-card" style={{ padding: '10px 12px' }}>
                          <div className="row between wrap">
                            <strong>{r.fileTypeName}</strong>
                            <StatusBadge
                              variant={
                                r.requirementMode === 'REQUIRED'
                                  ? 'info'
                                  : r.requirementMode === 'OPTIONAL'
                                    ? 'neutral'
                                    : 'warning'
                              }
                            >
                              {`${formatModeLabel(r.requirementMode)} (${r.minCount}–${r.maxCount})`}
                            </StatusBadge>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>

              {totalCount > 0 && (
                <PaginationBar
                  currentPage={page}
                  totalPages={totalPages}
                  totalItems={totalCount}
                  pageLabel={`${c.pageWord} ${page} ${c.ofWord} ${totalPages}`}
                  previousLabel={commonStrings.paginationPrev}
                  nextLabel={commonStrings.paginationNext}
                  onPageChange={setPage}
                />
              )}
            </>
          )}
        </section>
      )}

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
