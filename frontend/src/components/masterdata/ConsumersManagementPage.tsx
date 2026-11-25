import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ConsumerCategoryItem,
  ConsumerItem,
  createConsumer,
  createConsumerCategory,
  listConsumerCategories,
  listConsumers,
  updateConsumer,
  updateConsumerCategory,
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

interface ConsumersManagementPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type ActiveTab = 'consumers' | 'categories';
type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    tabConsumers: 'الجهات المستهلكة',
    tabCategories: 'تصنيفات الجهات (مستوى واحد)',
    consTitle: 'الجهات المستهلكة (UI-02 · M03)',
    consDesc: 'إدارة الجهات المستهلكة وربط كل جهة بتصنيف واحد دون هيكل تنظيمي متعدد المستويات.',
    catTitle: 'تصنيفات الجهات المستهلكة (UI-02 · M03)',
    catDesc: 'تصنيفات مستقلة مسطحة للجهات (مثل البنوك وأنظمة التجار) مع حماية المراجع عند التعطيل.',
    noViewTitle: 'صلاحية عرض الجهات المستهلكة غير متوفرة',
    noViewDesc: 'يتطلب عرض الجهات المستهلكة وتصنيفاتها توفر صلاحية consumers.view.',
    addConsBtn: 'إضافة جهة مستهلكة',
    addCatBtn: 'إضافة تصنيف جهات',
    searchPlaceholder: 'ابحث بالاسم...',
    searchAria: 'البحث في القائمة',
    filterAria: 'تصفية حسب التصنيف أو الحالة',
    filterAll: 'كل التصنيفات',
    filterAllStatus: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'الاسم',
    colCategory: 'التصنيف',
    colUsers: 'المستخدمون',
    colDocs: 'الوثائق',
    colConsumersCount: 'الجهات المسجلة',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createConsBadge: 'إضافة جهة مستهلكة · UI-02 / M03',
    editConsBadge: 'تعديل جهة مستهلكة · UI-02 / M03',
    createCatBadge: 'إضافة تصنيف جهات · UI-02 / M03',
    editCatBadge: 'تعديل تصنيف جهات · UI-02 / M03',
    createConsTitle: 'إضافة جهة مستهلكة جديدة',
    editConsTitle: 'تعديل بيانات الجهة المستهلكة',
    createCatTitle: 'إضافة تصنيف جهات جديد',
    editCatTitle: 'تعديل تصنيف الجهات',
    consFormSub: 'اختر التصنيف الفعّال ثم أدخل اسم الجهة المستهلكة.',
    catFormSub: 'تصنيف مستقل من مستوى واحد فقط.',
    categoryLabel: 'التصنيف',
    categoryPlaceholder: 'اختر تصنيفًا فعّالًا...',
    nameLabel: 'الاسم',
    activeLabel: 'السجل فعّال',
    requiredCatError: 'يرجى اختيار تصنيف فعّال.',
    requiredNameError: 'يرجى إدخال الاسم (حرفان على الأقل).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تمت الإضافة بنجاح',
    createSuccessMsg: 'تم حفظ البيانات في قاعدة البيانات وتسجيل العملية في سجل التدقيق.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم حفظ التعديلات وتحديث سجل التدقيق.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateMsg: 'سيتم التحقق من عدم وجود مستخدمين فعّالين أو جهات/وثائق نشطة مرتبطة قبل التعطيل. هل تؤكد؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا السجل؟',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    loadingText: 'جارٍ تحميل البيانات...',
    emptyTitle: 'لا توجد سجلات مطابقة',
    emptyDesc: 'عدّل معايير البحث أو أضف سجلًا جديدًا.',
    errorTitle: 'تعذر تحميل البيانات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    tabConsumers: 'Consumer Entities',
    tabCategories: 'Consumer Categories (Flat)',
    consTitle: 'Consumer Entities (UI-02 · M03)',
    consDesc: 'Manage consumer organizations linked to a single flat category level.',
    catTitle: 'Consumer Categories (UI-02 · M03)',
    catDesc: 'Flat independent consumer categories (e.g., Banks, Merchants) with reference protection.',
    noViewTitle: 'Consumers View Unavailable',
    noViewDesc: 'consumers.view permission is required to view consumers and categories.',
    addConsBtn: 'Add Consumer Entity',
    addCatBtn: 'Add Consumer Category',
    searchPlaceholder: 'Search by name...',
    searchAria: 'Search records',
    filterAria: 'Filter by category or status',
    filterAll: 'All Categories',
    filterAllStatus: 'All Statuses',
    filterActive: 'Active Only',
    filterInactive: 'Inactive Only',
    colName: 'Name',
    colCategory: 'Category',
    colUsers: 'Users',
    colDocs: 'Docs',
    colConsumersCount: 'Consumers',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createConsBadge: 'Create Consumer Entity · UI-02 / M03',
    editConsBadge: 'Edit Consumer Entity · UI-02 / M03',
    createCatBadge: 'Create Consumer Category · UI-02 / M03',
    editCatBadge: 'Edit Consumer Category · UI-02 / M03',
    createConsTitle: 'Create Consumer Entity',
    editConsTitle: 'Edit Consumer Entity',
    createCatTitle: 'Create Consumer Category',
    editCatTitle: 'Edit Consumer Category',
    consFormSub: 'Select an active category and enter the consumer entity name.',
    catFormSub: 'Single-level flat classification.',
    categoryLabel: 'Category',
    categoryPlaceholder: 'Select active category...',
    nameLabel: 'Name',
    activeLabel: 'Record is Active',
    requiredCatError: 'Please select an active category.',
    requiredNameError: 'Name is required (at least 2 characters).',
    validationTitle: 'Unable to Save',
    validationMsg: 'Please review required fields.',
    createSuccessTitle: 'Record Created',
    createSuccessMsg: 'Record saved and logged in the audit trail.',
    editSuccessTitle: 'Record Updated',
    editSuccessMsg: 'Changes saved and logged in the audit trail.',
    toggleTitle: 'Confirm Status Change',
    deactivateMsg: 'Deactivation checks that no active users, entities, or API documents depend on this record. Continue?',
    activateMsg: 'Re-activate this record?',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    loadingText: 'Loading records...',
    emptyTitle: 'No matching records found',
    emptyDesc: 'Adjust filters or add a new record.',
    errorTitle: 'Failed to Load Records',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function ConsumersManagementPage({ lang, permissions, onSessionExpired }: ConsumersManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canView = permissions.some((p) => p.toLowerCase() === 'consumers.view');
  const canManage = permissions.some((p) => p.toLowerCase() === 'consumers.manage');

  const [tab, setTab] = useState<ActiveTab>('consumers');
  const [mode, setMode] = useState<PageMode>('list');
  const [editingId, setEditingId] = useState<string | null>(null);

  const [consumers, setConsumers] = useState<ConsumerItem[]>([]);
  const [categories, setCategories] = useState<ConsumerCategoryItem[]>([]);
  const [allCategories, setAllCategories] = useState<ConsumerCategoryItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formCategoryId, setFormCategoryId] = useState('');
  const [formName, setFormName] = useState('');
  const [formActive, setFormActive] = useState(true);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modal, setModal] = useState<ModalState | null>(null);

  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadData = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      const catCatalogRes = await listConsumerCategories({ page: 1, pageSize: 100, lang });
      setAllCategories(catCatalogRes.items);

      if (tab === 'consumers') {
        const res = await listConsumers({
          page,
          pageSize: 10,
          search,
          categoryId: categoryFilter || undefined,
          isActive: activeFilter,
          lang,
        });
        setConsumers(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      } else {
        const res = await listConsumerCategories({ page, pageSize: 10, search, isActive: activeFilter, lang });
        setCategories(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      }
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
  }, [canView, tab, page, search, categoryFilter, activeFilter, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list') {
      void loadData();
    }
  }, [mode, loadData]);

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

  const handleSave = async () => {
    const errs: Record<string, string> = {};
    if (formName.trim().length < 2) errs.name = copy.requiredNameError;
    if (tab === 'consumers' && !formCategoryId) errs.categoryId = copy.requiredCatError;

    if (Object.keys(errs).length > 0) {
      setFieldErrors(errs);
      setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMsg });
      return;
    }

    try {
      if (tab === 'consumers') {
        if (mode === 'create') {
          await createConsumer({ categoryId: formCategoryId, name: formName.trim() }, lang);
          setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
        } else if (editingId) {
          await updateConsumer(
            editingId,
            { categoryId: formCategoryId, name: formName.trim(), isActive: formActive },
            lang
          );
          setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
        }
      } else {
        if (mode === 'create') {
          await createConsumerCategory(formName.trim(), lang);
          setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
        } else if (editingId) {
          await updateConsumerCategory(editingId, { name: formName.trim(), isActive: formActive }, lang);
          setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
        }
      }
      setMode('list');
      await loadData();
    } catch (err) {
      handleApiError(err);
    }
  };

  const consumerColumns: DataTableColumn<ConsumerItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      { key: 'cat', header: copy.colCategory, render: (row) => <StatusBadge variant="info">{row.categoryName}</StatusBadge> },
      { key: 'users', header: copy.colUsers, render: (row) => <code className="ltr-code">{row.assignedUsersCount}</code> },
      { key: 'docs', header: copy.colDocs, render: (row) => <code className="ltr-code">{row.documentsCount}</code> },
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
                  setEditingId(row.id);
                  setFormCategoryId(row.categoryId);
                  setFormName(row.name);
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
                        await updateConsumer(row.id, { isActive: !row.isActive }, lang);
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

  const categoryColumns: DataTableColumn<ConsumerCategoryItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      {
        key: 'consumers',
        header: copy.colConsumersCount,
        render: (row) => <code className="ltr-code">{row.consumersCount}</code>,
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
                  setEditingId(row.id);
                  setFormName(row.name);
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
                        await updateConsumerCategory(row.id, { isActive: !row.isActive }, lang);
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
                  setEditingId(null);
                  setFormCategoryId(
                    categoryFilter || (allCategories.find((c) => c.isActive)?.id ?? '')
                  );
                  setFormName('');
                  setFormActive(true);
                  setFieldErrors({});
                  setMode('create');
                }}
              >
                {tab === 'consumers' ? copy.addConsBtn : copy.addCatBtn}
              </Button>
            </div>
          )}
          <div className={tab === 'consumers' ? 'consumers-cols' : ''}>
          {tab === 'consumers' && (
            <section className="surface-card panel">
              <div className="panel-head">
                <h3 className="panel-title">{lang === 'ar' ? 'التصنيفات' : 'Categories'}</h3>
                {canManage && (
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => {
                      setTab('categories');
                      setEditingId(null);
                      setFormName('');
                      setFormActive(true);
                      setFieldErrors({});
                      setMode('create');
                    }}
                  >
                    + {lang === 'ar' ? 'تصنيف' : 'Category'}
                  </Button>
                )}
              </div>
              <div className="stack">
                <button
                  type="button"
                  className={`pager-link consumer-category ${!categoryFilter ? 'is-active' : ''}`}
                  onClick={() => {
                    setCategoryFilter('');
                    setPage(1);
                  }}
                >
                  <b>{copy.filterAll}</b>
                  <span className="tag">{totalCount}</span>
                </button>
                {allCategories.map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    className={`pager-link consumer-category ${categoryFilter === c.id ? 'is-active' : ''}`}
                    aria-pressed={categoryFilter === c.id}
                    onClick={() => {
                      setCategoryFilter(c.id);
                      setPage(1);
                    }}
                  >
                    <b>{c.name}</b>
                    <span className="tag">{c.consumersCount}</span>
                  </button>
                ))}
              </div>
            </section>
          )}

          <section className="surface-card panel">
            <div className="tabs">
              <button
                type="button"
                className={`tab ${tab === 'consumers' ? 'active' : ''}`}
                onClick={() => {
                  setTab('consumers');
                  setPage(1);
                  setSearch('');
                }}
              >
                {copy.tabConsumers}
              </button>
              <button
                type="button"
                className={`tab ${tab === 'categories' ? 'active' : ''}`}
                onClick={() => {
                  setTab('categories');
                  setPage(1);
                  setSearch('');
                }}
              >
                {copy.tabCategories}
              </button>
            </div>

            <div className="panel-head">
              <div>
                <h2 className="panel-title heading-font">
                  {tab === 'consumers' ? copy.consTitle : copy.catTitle}
                </h2>
                <p className="panel-desc">
                  {tab === 'consumers' ? copy.consDesc : copy.catDesc}
                </p>
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
            filterValue={tab === 'consumers' ? categoryFilter : activeFilter}
            filterAriaLabel={copy.filterAria}
            filterPlaceholder={tab === 'consumers' ? copy.filterAll : copy.filterAllStatus}
            filterOptions={
              tab === 'consumers'
                ? allCategories.map((c) => ({ value: c.id, label: c.name }))
                : [
                    { value: 'true', label: copy.filterActive },
                    { value: 'false', label: copy.filterInactive },
                  ]
            }
            onFilterChange={(val) => {
              if (tab === 'consumers') setCategoryFilter(val);
              else setActiveFilter((val as '' | 'true' | 'false') || '');
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
              {tab === 'consumers' ? (
                <DataTable
                  columns={consumerColumns}
                  rows={consumers}
                  getRowKey={(r) => r.id}
                  isLoading={loading}
                  loadingTitle={copy.loadingText}
                  emptyTitle={copy.emptyTitle}
                  emptyDescription={copy.emptyDesc}
                />
              ) : (
                <DataTable
                  columns={categoryColumns}
                  rows={categories}
                  getRowKey={(r) => r.id}
                  isLoading={loading}
                  loadingTitle={copy.loadingText}
                  emptyTitle={copy.emptyTitle}
                  emptyDescription={copy.emptyDesc}
                />
              )}
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
          </div>
        </>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            tab === 'consumers'
              ? mode === 'create'
                ? copy.createConsBadge
                : copy.editConsBadge
              : mode === 'create'
                ? copy.createCatBadge
                : copy.editCatBadge
          }
          formTitle={
            tab === 'consumers'
              ? mode === 'create'
                ? copy.createConsTitle
                : copy.editConsTitle
              : mode === 'create'
                ? copy.createCatTitle
                : copy.editCatTitle
          }
          formSubtitle={tab === 'consumers' ? copy.consFormSub : copy.catFormSub}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSave()}
        >
          <div className="form-grid-2">
            {tab === 'consumers' && (
              <FormField
                id="consumer-category-select"
                label={copy.categoryLabel}
                required
                error={fieldErrors.categoryId}
              >
                <SelectInput
                  id="consumer-category-select"
                  placeholder={copy.categoryPlaceholder}
                  value={formCategoryId}
                  hasError={Boolean(fieldErrors.categoryId)}
                  options={allCategories
                    .filter((c) => c.isActive || c.id === formCategoryId)
                    .map((c) => ({ value: c.id, label: c.name }))}
                  onChange={(e) => {
                    setFormCategoryId(e.target.value);
                    setFieldErrors((prev) => ({ ...prev, categoryId: '' }));
                  }}
                />
              </FormField>
            )}

            <FormField id="consumer-name-input" label={copy.nameLabel} required error={fieldErrors.name}>
              <TextInput
                ref={nameRef}
                id="consumer-name-input"
                value={formName}
                hasError={Boolean(fieldErrors.name)}
                onChange={(e) => {
                  setFormName(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, name: '' }));
                }}
              />
            </FormField>

            {mode === 'edit' && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, paddingTop: 24 }}>
                <input
                  id="consumer-active-input"
                  type="checkbox"
                  checked={formActive}
                  onChange={(e) => setFormActive(e.target.checked)}
                />
                <label htmlFor="consumer-active-input" style={{ fontSize: 13.5 }}>
                  {copy.activeLabel}
                </label>
              </div>
            )}
          </div>
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

