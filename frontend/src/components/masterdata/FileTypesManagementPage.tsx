import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  FileTypeCatalogEntry,
  FileTypeItem,
  createFileType,
  getCommonFileTypesCatalog,
  listFileTypes,
  updateFileType,
} from '../../api/masterDataApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  PaginationBar,
  SearchFilterBar,
  SelectInput,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
} from '../ui';

interface FileTypesManagementPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    title: 'قاموس أنواع الملفات والامتدادات المعتمدة (UI-04 · M04)',
    desc: 'إدارة أنواع الملفات انطلاقًا من قاموس الأنواع الشائعة فقط مع تعبئة الامتدادات تلقائيًا وتحديد الحد الأقصى للحجم وقالب Word لنوع Word فقط (الإلزامية والأعداد تُضبط في قوالب المتطلبات).',
    noViewTitle: 'صلاحية عرض أنواع الملفات غير متوفرة',
    noViewDesc: 'يتطلب عرض أنواع الملفات توفر صلاحية file_types.view.',
    addBtn: 'تعريف نوع ملف من القاموس',
    searchPlaceholder: 'ابحث بالاسم أو رمز النوع...',
    searchAria: 'البحث في أنواع الملفات',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'الاسم الظاهر',
    colCode: 'رمز القاموس',
    colExt: 'الامتدادات المسموحة',
    colMaxSize: 'الحد الأقصى',
    colWordTpl: 'قالب Word',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    wordTplSet: 'مرتبط بقالب',
    wordTplNA: 'غير منطبق',
    wordTplEmpty: 'اختياري (غير محدد)',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createBadge: 'إضافة نوع ملف · UI-04 / M04',
    editBadge: 'تعديل نوع ملف · UI-04 / M04',
    createTitle: 'تعريف نوع ملف من قائمة الأنواع الشائعة',
    editTitle: 'تعديل إعدادات نوع الملف',
    formSub: 'تُملأ الامتدادات تلقائيًا ويُحظر أي امتداد خارج الكتالوج المعتمد. يظهر حقل قالب Word عند اختيار نوع Word فقط.',
    catalogLabel: 'نوع الملف القياسي (من القاموس المعتمد)',
    catalogPlaceholder: 'اختر نوعًا قياسيًا...',
    displayNameLabel: 'الاسم الظاهر',
    maxSizeMbLabel: 'الحد الأقصى للحجم (بالميجابايت MB)',
    autoExtensionsLabel: 'الامتدادات التلقائية المعتمدة:',
    wordTemplateLabel: 'معرّف كائن قالب Word (UUID — لنوع WORD_DOC فقط، اختياري)',
    activeLabel: 'نوع الملف فعّال',
    backBtn: 'العودة إلى أنواع الملفات',
    saveCreateBtn: 'حفظ وتعريف النوع',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    requiredCatalogError: 'يرجى اختيار نوع ملف من القائمة المعتمدة.',
    requiredNameError: 'يرجى إدخال الاسم الظاهر (حرفان على الأقل).',
    requiredSizeError: 'يرجى إدخال حجم صحيح بالميجابايت (1 إلى 100 MB).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تم تعريف نوع الملف بنجاح',
    createSuccessMsg: 'تم حفظ نوع الملف وامتداداته المعتمدة في قاعدة البيانات وتسجيله في سجل التدقيق.',
    editSuccessTitle: 'تم تحديث نوع الملف',
    editSuccessMsg: 'تم حفظ التعديلات وتسجيلها في سجل التدقيق.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateMsg: 'سيتم التحقق من عدم ارتباط نوع الملف بقالب متطلبات فعّال قبل التعطيل. هل تؤكد؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا النوع؟',
    loadingText: 'جارٍ تحميل أنواع الملفات...',
    emptyTitle: 'لا توجد أنواع ملفات مطابقة',
    emptyDesc: 'عدّل معايير البحث أو عرّف نوعًا من قاموس الأنواع الشائعة.',
    errorTitle: 'تعذر تحميل أنواع الملفات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    title: 'File Types & Approved Extensions Dictionary (UI-04 · M04)',
    desc: 'Select common file types with auto-populated verified extensions, max size limits, and Word template support exclusively for Word documents (requirements/counts are configured in Templates).',
    noViewTitle: 'File Types View Unavailable',
    noViewDesc: 'file_types.view permission is required to view file types.',
    addBtn: 'Define File Type from Catalog',
    searchPlaceholder: 'Search by display name or code...',
    searchAria: 'Search file types',
    filterAria: 'Filter by status',
    filterAll: 'All Statuses',
    filterActive: 'Active Only',
    filterInactive: 'Inactive Only',
    colName: 'Display Name',
    colCode: 'Catalog Code',
    colExt: 'Allowed Extensions',
    colMaxSize: 'Max Size',
    colWordTpl: 'Word Template',
    colStatus: 'Status',
    colActions: 'Actions',
    wordTplSet: 'Template Linked',
    wordTplNA: 'N/A',
    wordTplEmpty: 'Optional (Unset)',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createBadge: 'Create File Type · UI-04 / M04',
    editBadge: 'Edit File Type · UI-04 / M04',
    createTitle: 'Define File Type from Common Catalog',
    editTitle: 'Edit File Type Settings',
    formSub: 'Extensions auto-populate from the verified catalog. Word template applies only to WORD_DOC.',
    catalogLabel: 'Common File Type (Approved Catalog)',
    catalogPlaceholder: 'Select common type...',
    displayNameLabel: 'Display Name',
    maxSizeMbLabel: 'Maximum File Size (MB)',
    autoExtensionsLabel: 'Verified Auto-Populated Extensions:',
    wordTemplateLabel: 'Word Template Object ID (Optional UUID — WORD_DOC only)',
    activeLabel: 'File Type is Active',
    backBtn: 'Back to File Types',
    saveCreateBtn: 'Create File Type',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    requiredCatalogError: 'Please choose a file type from the catalog.',
    requiredNameError: 'Display name is required (at least 2 characters).',
    requiredSizeError: 'Enter a valid size between 1 and 100 MB.',
    validationTitle: 'Unable to Save File Type',
    validationMsg: 'Please review required fields.',
    createSuccessTitle: 'File Type Defined',
    createSuccessMsg: 'File type and verified extensions saved to database and logged in the audit trail.',
    editSuccessTitle: 'File Type Updated',
    editSuccessMsg: 'Changes saved and logged in the audit trail.',
    toggleTitle: 'Confirm Status Change',
    deactivateMsg: 'Deactivation checks that no active file requirement template references this type. Continue?',
    activateMsg: 'Re-activate this file type?',
    loadingText: 'Loading file types...',
    emptyTitle: 'No matching file types found',
    emptyDesc: 'Adjust filters or define a type from the approved catalog.',
    errorTitle: 'Failed to Load File Types',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function FileTypesManagementPage({ lang, permissions, onSessionExpired }: FileTypesManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canView = permissions.some((p) => p.toLowerCase() === 'file_types.view');
  const canManage = permissions.some((p) => p.toLowerCase() === 'file_types.manage');

  const [mode, setMode] = useState<PageMode>('list');
  const [editingItem, setEditingItem] = useState<FileTypeItem | null>(null);

  const [items, setItems] = useState<FileTypeItem[]>([]);
  const [catalog, setCatalog] = useState<FileTypeCatalogEntry[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formCatalogCode, setFormCatalogCode] = useState('');
  const [formDisplayName, setFormDisplayName] = useState('');
  const [formMaxSizeMb, setFormMaxSizeMb] = useState('10');
  const [formWordTplId, setFormWordTplId] = useState('');
  const [formActive, setFormActive] = useState(true);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modal, setModal] = useState<ModalState | null>(null);

  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadData = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      const [listRes, catRes] = await Promise.all([
        listFileTypes({ page, pageSize: 10, search, isActive: activeFilter, lang }),
        getCommonFileTypesCatalog(lang),
      ]);
      setItems(listRes.items);
      setTotalPages(listRes.totalPages);
      setTotalCount(listRes.totalCount);
      setCatalog(catRes);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const apiErr = err instanceof ApiProblemError ? err : null;
      setLoadError({ message: apiErr?.message ?? copy.errorTitle, traceId: apiErr?.traceId });
    } finally {
      setLoading(false);
    }
  }, [canView, page, search, activeFilter, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list') {
      void loadData();
    }
  }, [mode, loadData]);

  const selectedCatalogEntry = useMemo(
    () => catalog.find((c) => c.catalogCode === formCatalogCode) ?? null,
    [catalog, formCatalogCode]
  );

  const handleApiError = (err: unknown) => {
    if (err instanceof ApiProblemError && err.status === 401) {
      onSessionExpired();
      return;
    }
    const apiErr = err instanceof ApiProblemError ? err : null;
    setModal({
      variant: 'error',
      title: apiErr?.title ?? copy.validationTitle,
      message: apiErr?.message ?? copy.validationMsg,
    });
  };

  const handleSelectCatalogCode = (code: string) => {
    setFormCatalogCode(code);
    setFieldErrors((prev) => ({ ...prev, catalogCode: '' }));
    const entry = catalog.find((c) => c.catalogCode === code);
    if (entry) {
      setFormDisplayName(lang === 'ar' ? entry.defaultNameAr : entry.defaultNameEn);
      setFormMaxSizeMb(String(Math.max(1, Math.round(entry.defaultMaxSizeBytes / (1024 * 1024)))));
      if (!entry.supportsWordTemplate) {
        setFormWordTplId('');
      }
    }
  };

  const handleSave = async () => {
    const errs: Record<string, string> = {};
    if (mode === 'create' && !formCatalogCode) errs.catalogCode = copy.requiredCatalogError;
    if (formDisplayName.trim().length < 2) errs.displayName = copy.requiredNameError;
    const sizeNum = Number(formMaxSizeMb);
    if (!Number.isFinite(sizeNum) || sizeNum < 1 || sizeNum > 100) errs.maxSizeMb = copy.requiredSizeError;

    if (Object.keys(errs).length > 0) {
      setFieldErrors(errs);
      setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMsg });
      return;
    }

    const maxBytes = Math.round(sizeNum * 1024 * 1024);
    try {
      if (mode === 'create') {
        await createFileType(
          {
            catalogCode: formCatalogCode,
            displayName: formDisplayName.trim(),
            maxSizeBytes: maxBytes,
            wordTemplateObjectId: selectedCatalogEntry?.supportsWordTemplate
              ? formWordTplId.trim() || null
              : null,
            extensions: selectedCatalogEntry?.allowedExtensions,
          },
          lang
        );
        setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
      } else if (editingItem) {
        await updateFileType(
          editingItem.id,
          {
            displayName: formDisplayName.trim(),
            maxSizeBytes: maxBytes,
            wordTemplateObjectId: editingItem.supportsWordTemplate ? formWordTplId.trim() || null : null,
            clearWordTemplate: editingItem.supportsWordTemplate && !formWordTplId.trim() && Boolean(editingItem.wordTemplateObjectId),
            isActive: formActive,
          },
          lang
        );
        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
      }
      setMode('list');
      await loadData();
    } catch (err) {
      handleApiError(err);
    }
  };

  const columns: DataTableColumn<FileTypeItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.displayName}</strong> },
      { key: 'code', header: copy.colCode, render: (row) => <code className="ltr-code">{row.catalogCode}</code> },
      {
        key: 'ext',
        header: copy.colExt,
        render: (row) => (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
            {row.extensions.map((ext) => (
              <StatusBadge key={ext} variant="neutral">
                <code className="ltr-code">{ext}</code>
              </StatusBadge>
            ))}
          </div>
        ),
      },
      {
        key: 'size',
        header: copy.colMaxSize,
        render: (row) => <code className="ltr-code">{Math.round(row.maxSizeBytes / (1024 * 1024))} MB</code>,
      },
      {
        key: 'wordTpl',
        header: copy.colWordTpl,
        render: (row) =>
          !row.supportsWordTemplate ? (
            <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{copy.wordTplNA}</span>
          ) : row.wordTemplateObjectId ? (
            <StatusBadge variant="info">{copy.wordTplSet}</StatusBadge>
          ) : (
            <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{copy.wordTplEmpty}</span>
          ),
      },
      {
        key: 'status',
        header: copy.colStatus,
        render: (row) => (
          <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
            {row.isActive ? copy.activeBadge : copy.inactiveBadge}
          </StatusBadge>
        ),
      },
      {
        key: 'actions',
        header: copy.colActions,
        render: (row) =>
          canManage ? (
            <div className="button-row" style={{ gap: 6 }}>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingItem(row);
                  setFormCatalogCode(row.catalogCode);
                  setFormDisplayName(row.displayName);
                  setFormMaxSizeMb(String(Math.max(1, Math.round(row.maxSizeBytes / (1024 * 1024)))));
                  setFormWordTplId(row.wordTemplateObjectId ?? '');
                  setFormActive(row.isActive);
                  setFieldErrors({});
                  setMode('edit');
                }}
              >
                {copy.editBtn}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  setModal({
                    variant: 'confirm',
                    title: copy.toggleTitle,
                    message: row.isActive ? copy.deactivateMsg : copy.activateMsg,
                    onConfirmAction: async () => {
                      try {
                        await updateFileType(row.id, { isActive: !row.isActive }, lang);
                        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
                        await loadData();
                      } catch (err) {
                        handleApiError(err);
                      }
                    },
                  })
                }
              >
                {row.isActive ? copy.deactivateBtn : copy.activateBtn}
              </Button>
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canManage, lang]
  );

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={copy.noViewTitle} message={copy.noViewDesc} />
      </section>
    );
  }

  return (
    <>
      {mode === 'list' ? (
        <>
          {canManage && (
            <div className="page-actions">
              <Button
                variant="primary"
                onClick={() => {
                  setEditingItem(null);
                  setFormCatalogCode('');
                  setFormDisplayName('');
                  setFormMaxSizeMb('10');
                  setFormWordTplId('');
                  setFormActive(true);
                  setFieldErrors({});
                  setMode('create');
                }}
              >
                {copy.addBtn}
              </Button>
            </div>
          )}
          <section className="surface-card panel">
            <div className="panel-head">
              <div>
                <h2 className="panel-title heading-font">{copy.title}</h2>
                <p className="panel-desc">{copy.desc}</p>
              </div>
            </div>

          <SearchFilterBar
            searchValue={search}
            searchPlaceholder={copy.searchPlaceholder}
            searchAriaLabel={copy.searchAria}
            onSearchChange={(val) => {
              setSearch(val);
              setPage(1);
            }}
            filterValue={activeFilter}
            filterAriaLabel={copy.filterAria}
            filterPlaceholder={copy.filterAll}
            filterOptions={[
              { value: 'true', label: copy.filterActive },
              { value: 'false', label: copy.filterInactive },
            ]}
            onFilterChange={(val) => {
              setActiveFilter((val as '' | 'true' | 'false') || '');
              setPage(1);
            }}
          />

          {loadError ? (
            <ErrorState
              title={copy.errorTitle}
              message={loadError.message}
              traceId={loadError.traceId}
              retryLabel={copy.retryBtn}
              onRetry={() => void loadData()}
            />
          ) : (
            <>
              <DataTable
                columns={columns}
                rows={items}
                getRowKey={(r) => r.id}
                isLoading={loading}
                loadingTitle={copy.loadingText}
                emptyTitle={copy.emptyTitle}
                emptyDescription={copy.emptyDesc}
              />
              {totalCount > 0 && (
                <PaginationBar
                  currentPage={page}
                  totalPages={totalPages}
                  totalItems={totalCount}
                  pageLabel={`${copy.pagePrefix} ${page} ${copy.ofWord} ${totalPages}`}
                  previousLabel={copy.prevBtn}
                  nextLabel={copy.nextBtn}
                  onPageChange={setPage}
                />
              )}
            </>
          )}
          </section>
        </>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={mode === 'create' ? copy.createBadge : copy.editBadge}
          formTitle={mode === 'create' ? copy.createTitle : copy.editTitle}
          formSubtitle={copy.formSub}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSave()}
        >
          <div className="form-grid-2">
            <FormField
              id="filetype-catalog-select"
              label={copy.catalogLabel}
              required={mode === 'create'}
              error={fieldErrors.catalogCode}
            >
              <SelectInput
                id="filetype-catalog-select"
                placeholder={copy.catalogPlaceholder}
                disabled={mode === 'edit'}
                value={formCatalogCode}
                hasError={Boolean(fieldErrors.catalogCode)}
                options={catalog
                  .filter(
                    (c) =>
                      mode === 'edit' ||
                      !items.some((existing) => existing.catalogCode.toUpperCase() === c.catalogCode.toUpperCase())
                  )
                  .map((c) => ({
                    value: c.catalogCode,
                    label: `${lang === 'ar' ? c.defaultNameAr : c.defaultNameEn} (${c.allowedExtensions.join(', ')})`,
                  }))}
                onChange={(e) => handleSelectCatalogCode(e.target.value)}
              />
            </FormField>

            <FormField
              id="filetype-name-input"
              label={copy.displayNameLabel}
              required
              error={fieldErrors.displayName}
            >
              <TextInput
                ref={nameRef}
                id="filetype-name-input"
                value={formDisplayName}
                hasError={Boolean(fieldErrors.displayName)}
                onChange={(e) => {
                  setFormDisplayName(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, displayName: '' }));
                }}
              />
            </FormField>

            <FormField
              id="filetype-maxsize-input"
              label={copy.maxSizeMbLabel}
              required
              error={fieldErrors.maxSizeMb}
            >
              <TextInput
                id="filetype-maxsize-input"
                type="number"
                min={1}
                max={100}
                ltrCode
                value={formMaxSizeMb}
                hasError={Boolean(fieldErrors.maxSizeMb)}
                onChange={(e) => {
                  setFormMaxSizeMb(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, maxSizeMb: '' }));
                }}
              />
            </FormField>

            {mode === 'edit' && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, paddingTop: 24 }}>
                <input
                  id="filetype-active-input"
                  type="checkbox"
                  checked={formActive}
                  onChange={(e) => setFormActive(e.target.checked)}
                />
                <label htmlFor="filetype-active-input" style={{ fontSize: 13.5 }}>
                  {copy.activeLabel}
                </label>
              </div>
            )}
          </div>

          {selectedCatalogEntry && (
            <div
              style={{
                marginTop: 14,
                padding: 12,
                borderRadius: 8,
                border: '1px solid var(--border-color)',
                background: 'var(--bg-subtle)',
              }}
            >
              <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 6 }}>
                {copy.autoExtensionsLabel}
              </div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                {selectedCatalogEntry.allowedExtensions.map((ext) => (
                  <StatusBadge key={ext} variant="info">
                    <code className="ltr-code">{ext}</code>
                  </StatusBadge>
                ))}
              </div>
            </div>
          )}

          {selectedCatalogEntry?.supportsWordTemplate && (
            <div style={{ marginTop: 14 }}>
              <FormField id="filetype-word-template" label={copy.wordTemplateLabel}>
                <TextInput
                  id="filetype-word-template"
                  ltrCode
                  value={formWordTplId}
                  onChange={(e) => setFormWordTplId(e.target.value)}
                />
              </FormField>
            </div>
          )}
        </StandaloneFormPage>
      )}

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
          if (active.variant === 'error') {
            nameRef.current?.focus();
          }
        }}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}

