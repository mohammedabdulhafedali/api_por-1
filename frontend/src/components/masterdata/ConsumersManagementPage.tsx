import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ConsumerCategoryItem,
  ConsumerItem,
  createConsumer,
  createConsumerCategory,
  deleteConsumer,
  deleteConsumerCategory,
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
  ReferenceIcon,
  RowActionButton,
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

type FormTarget = 'consumer' | 'category';
type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    categoriesSidebarTitle: 'التصنيفات',
    noViewTitle: 'صلاحية عرض الجهات المستهلكة غير متوفرة',
    noViewDesc: 'يتطلب عرض الجهات المستهلكة وتصنيفاتها توفر صلاحية العرض على حسابك.',
    addConsAria: 'إضافة جهة مستهلكة',
    addCatAria: 'إضافة تصنيف جهات',
    editCatAria: 'تعديل التصنيف',
    deleteCatAria: 'حذف التصنيف',
    filterAll: 'كل التصنيفات',
    colName: 'الاسم',
    colCategory: 'التصنيف',
    colUsers: 'المستخدمون',
    colDocs: 'الوثائق',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    createConsBadge: 'إضافة جهة مستهلكة',
    editConsBadge: 'تعديل جهة مستهلكة',
    createCatBadge: 'إضافة تصنيف جهات',
    editCatBadge: 'تعديل تصنيف جهات',
    createConsTitle: 'إضافة جهة مستهلكة جديدة',
    editConsTitle: 'تعديل بيانات الجهة المستهلكة',
    createCatTitle: 'إضافة تصنيف جهات جديد',
    editCatTitle: 'تعديل تصنيف الجهات',
    categoryLabel: 'التصنيف',
    categoryPlaceholder: 'اختر تصنيفًا فعّالًا...',
    nameLabel: 'الاسم',
    activeLabel: 'السجل فعّال',
    requiredCatError: 'يرجى اختيار تصنيف فعّال.',
    requiredNameError: 'يرجى إدخال الاسم (حرفان على الأقل).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تمت الإضافة بنجاح',
    createSuccessMsg: 'تم حفظ السجل بنجاح.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم حفظ التعديلات بنجاح.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateMsg: 'هل تؤكد تعطيل هذا السجل؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا السجل؟',
    deleteTitle: 'تأكيد الحذف',
    deleteMsg: 'هل تؤكد حذف هذا السجل نهائيًا؟',
    deleteSuccessTitle: 'تم الحذف بنجاح',
    deleteSuccessMsg: 'تم حذف السجل من النظام بنجاح.',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    loadingText: 'جارٍ تحميل البيانات...',
    emptyTitle: 'لا توجد جهات مسجلة',
    emptyDesc: 'اضغط على زر (+) لإضافة جهة مستهلكة جديدة.',
    errorTitle: 'تعذر تحميل البيانات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    categoriesSidebarTitle: 'Categories',
    noViewTitle: 'Consumers View Unavailable',
    noViewDesc: 'View permission is required to view consumers and categories.',
    addConsAria: 'Add Consumer Entity',
    addCatAria: 'Add Consumer Category',
    editCatAria: 'Edit Category',
    deleteCatAria: 'Delete Category',
    filterAll: 'All Categories',
    colName: 'Name',
    colCategory: 'Category',
    colUsers: 'Users',
    colDocs: 'Docs',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    createConsBadge: 'Create Consumer Entity',
    editConsBadge: 'Edit Consumer Entity',
    createCatBadge: 'Create Consumer Category',
    editCatBadge: 'Edit Consumer Category',
    createConsTitle: 'Create Consumer Entity',
    editConsTitle: 'Edit Consumer Entity',
    createCatTitle: 'Create Consumer Category',
    editCatTitle: 'Edit Consumer Category',
    categoryLabel: 'Category',
    categoryPlaceholder: 'Select active category...',
    nameLabel: 'Name',
    activeLabel: 'Record is Active',
    requiredCatError: 'Please select an active category.',
    requiredNameError: 'Name is required (at least 2 characters).',
    validationTitle: 'Unable to Save',
    validationMsg: 'Please review required fields.',
    createSuccessTitle: 'Record Created',
    createSuccessMsg: 'Record saved successfully.',
    editSuccessTitle: 'Record Updated',
    editSuccessMsg: 'Changes saved successfully.',
    toggleTitle: 'Confirm Status Change',
    deactivateMsg: 'Are you sure you want to deactivate this record?',
    activateMsg: 'Re-activate this record?',
    deleteTitle: 'Confirm Deletion',
    deleteMsg: 'Are you sure you want to permanently delete this record?',
    deleteSuccessTitle: 'Record Deleted',
    deleteSuccessMsg: 'Record has been permanently deleted.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    loadingText: 'Loading records...',
    emptyTitle: 'No consumer entities found',
    emptyDesc: 'Click the (+) button to add a new consumer entity.',
    errorTitle: 'Failed to Load Records',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function ConsumersManagementPage({
  lang,
  permissions,
  onSessionExpired,
}: ConsumersManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];
  const hasPerm = (code: string) => permissions.some((p) => p.toLowerCase() === code.toLowerCase());

  const canView = hasPerm('consumers.view');
  const canCreate = hasPerm('consumers.create');
  const canEdit = hasPerm('consumers.edit');
  const canDelete = hasPerm('consumers.delete');

  const [formTarget, setFormTarget] = useState<FormTarget>('consumer');
  const [mode, setMode] = useState<PageMode>('list');
  const [editingId, setEditingId] = useState<string | null>(null);

  const [consumers, setConsumers] = useState<ConsumerItem[]>([]);
  const [allCategories, setAllCategories] = useState<ConsumerCategoryItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [categoryFilter, setCategoryFilter] = useState('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formCategoryId, setFormCategoryId] = useState('');
  const [formName, setFormName] = useState('');
  const [formActive, setFormActive] = useState(true);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modal, setModal] = useState<ModalState | null>(null);
  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadAllCategoriesForSelect = useCallback(async () => {
    if (!canView) return;
    try {
      const res = await listConsumerCategories({ page: 1, pageSize: 100, lang });
      setAllCategories(res.items);
    } catch {
      // Handled by main loader
    }
  }, [canView, lang]);

  const loadData = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      await loadAllCategoriesForSelect();
      const res = await listConsumers({
        page,
        pageSize: 10,
        categoryId: categoryFilter || undefined,
        lang,
      });
      setConsumers(res.items);
      setTotalPages(res.totalPages);
      setTotalCount(res.totalCount);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const apiErr = err instanceof ApiProblemError ? err : null;
      setLoadError({
        message: apiErr?.message ?? copy.errorTitle,
        traceId: apiErr?.traceId,
      });
    } finally {
      setLoading(false);
    }
  }, [canView, page, categoryFilter, lang, onSessionExpired, copy.errorTitle, loadAllCategoriesForSelect]);

  useEffect(() => {
    if (mode === 'list' && canView) {
      void loadData();
    }
  }, [mode, canView, loadData]);

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

  const handleSaveForm = async () => {
    const errs: Record<string, string> = {};
    if (formTarget === 'consumer' && !formCategoryId) {
      errs.categoryId = copy.requiredCatError;
    }
    if (formName.trim().length < 2) {
      errs.name = copy.requiredNameError;
    }

    if (Object.keys(errs).length > 0) {
      setFieldErrors(errs);
      setModal({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMsg,
      });
      return;
    }

    try {
      if (formTarget === 'consumer') {
        if (mode === 'create') {
          await createConsumer({ categoryId: formCategoryId, name: formName.trim() }, lang);
        } else if (editingId) {
          await updateConsumer(
            editingId,
            { categoryId: formCategoryId, name: formName.trim(), isActive: formActive },
            lang
          );
        }
      } else {
        if (mode === 'create') {
          await createConsumerCategory(formName.trim(), lang);
        } else if (editingId) {
          await updateConsumerCategory(
            editingId,
            { name: formName.trim(), isActive: formActive },
            lang
          );
        }
      }

      const wasCreate = mode === 'create';
      setMode('list');
      setModal({
        variant: 'success',
        title: wasCreate ? copy.createSuccessTitle : copy.editSuccessTitle,
        message: wasCreate ? copy.createSuccessMsg : copy.editSuccessMsg,
      });
      await loadData();
    } catch (err) {
      handleApiError(err);
    }
  };

  const consumerColumns: DataTableColumn<ConsumerItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      {
        key: 'category',
        header: copy.colCategory,
        render: (row) => (
          <StatusBadge variant={row.categoryIsActive ? 'info' : 'warning'}>
            {row.categoryName}
          </StatusBadge>
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
          canEdit || canDelete ? (
            <div className="row-actions">
              {canEdit && (
                <>
                  <RowActionButton
                    icon="edit"
                    label={copy.editBtn}
                    onClick={() => {
                      setFormTarget('consumer');
                      setEditingId(row.id);
                      setFormCategoryId(row.categoryId);
                      setFormName(row.name);
                      setFormActive(row.isActive);
                      setFieldErrors({});
                      setMode('edit');
                    }}
                  />
                  <RowActionButton
                    icon="power"
                    label={row.isActive ? copy.deactivateBtn : copy.activateBtn}
                    variant={row.isActive ? 'warning' : 'success'}
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
                  />
                </>
              )}
              {canDelete && (
                <RowActionButton
                  icon="trash"
                  label={copy.deleteBtn}
                  variant="danger"
                  onClick={() =>
                    setModal({
                      variant: 'confirm',
                      title: copy.deleteTitle,
                      message: copy.deleteMsg,
                      onConfirmAction: async () => {
                        try {
                          await deleteConsumer(row.id, lang);
                          setModal({
                            variant: 'success',
                            title: copy.deleteSuccessTitle,
                            message: copy.deleteSuccessMsg,
                          });
                          await loadData();
                        } catch (err) {
                          handleApiError(err);
                        }
                      },
                    })
                  }
                />
              )}
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, canDelete, lang]
  );

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={copy.noViewTitle} message={copy.noViewDesc} />
      </section>
    );
  }

  const totalAllConsumers = allCategories.reduce((sum, c) => sum + c.consumersCount, 0) || totalCount;

  return (
    <>
      {mode === 'list' ? (
        <div className="consumers-cols">
          <section className="surface-card panel clean-categories-panel">
            <div className="panel-head" style={{ marginBottom: 12 }}>
              <h3 className="panel-title">{copy.categoriesSidebarTitle}</h3>
              {canCreate && (
                <button
                  type="button"
                  className="compact-inline-add-btn"
                  aria-label={copy.addCatAria}
                  title={copy.addCatAria}
                  onClick={() => {
                    setFormTarget('category');
                    setEditingId(null);
                    setFormName('');
                    setFormActive(true);
                    setFieldErrors({});
                    setMode('create');
                  }}
                >
                  <ReferenceIcon name="plus" size={16} />
                </button>
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
                <span className="tag">{totalAllConsumers}</span>
              </button>

              {allCategories.map((c) => (
                <div
                  key={c.id}
                  className={`pager-link consumer-category category-item-row ${
                    categoryFilter === c.id ? 'is-active' : ''
                  }`}
                >
                  <button
                    type="button"
                    className="category-select-main-btn"
                    aria-pressed={categoryFilter === c.id}
                    onClick={() => {
                      setCategoryFilter(c.id);
                      setPage(1);
                    }}
                  >
                    <b>{c.name}</b>
                  </button>

                  <div className="category-item-end">
                    <span className="tag">{c.consumersCount}</span>
                    {(canEdit || canDelete) && (
                      <div className="category-mini-actions">
                        {canEdit && (
                          <button
                            type="button"
                            className="category-mini-icon-btn"
                            title={copy.editCatAria}
                            aria-label={`${copy.editCatAria} ${c.name}`}
                            onClick={() => {
                              setFormTarget('category');
                              setEditingId(c.id);
                              setFormName(c.name);
                              setFormActive(c.isActive);
                              setFieldErrors({});
                              setMode('edit');
                            }}
                          >
                            <ReferenceIcon name="edit" size={13} />
                          </button>
                        )}
                        {canDelete && (
                          <button
                            type="button"
                            className="category-mini-icon-btn danger"
                            title={copy.deleteCatAria}
                            aria-label={`${copy.deleteCatAria} ${c.name}`}
                            onClick={() =>
                              setModal({
                                variant: 'confirm',
                                title: copy.deleteTitle,
                                message: copy.deleteMsg,
                                onConfirmAction: async () => {
                                  try {
                                    await deleteConsumerCategory(c.id, lang);
                                    if (categoryFilter === c.id) {
                                      setCategoryFilter('');
                                    }
                                    setModal({
                                      variant: 'success',
                                      title: copy.deleteSuccessTitle,
                                      message: copy.deleteSuccessMsg,
                                    });
                                    await loadData();
                                  } catch (err) {
                                    handleApiError(err);
                                  }
                                },
                              })
                            }
                          >
                            <ReferenceIcon name="trash" size={13} />
                          </button>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="surface-card panel clean-table-panel">
            {canCreate && (
              <button
                type="button"
                className="compact-add-icon-btn"
                aria-label={copy.addConsAria}
                title={copy.addConsAria}
                onClick={() => {
                  setFormTarget('consumer');
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
                <ReferenceIcon name="plus" size={18} />
              </button>
            )}

            {loadError ? (
              <ErrorState
                title={copy.errorTitle}
                message={loadError.message}
                retryLabel={copy.retryBtn}
                onRetry={() => void loadData()}
              />
            ) : (
              <>
                <DataTable
                  columns={consumerColumns}
                  rows={consumers}
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
        </div>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            formTarget === 'consumer'
              ? mode === 'create'
                ? copy.createConsBadge
                : copy.editConsBadge
              : mode === 'create'
                ? copy.createCatBadge
                : copy.editCatBadge
          }
          formTitle={
            formTarget === 'consumer'
              ? mode === 'create'
                ? copy.createConsTitle
                : copy.editConsTitle
              : mode === 'create'
                ? copy.createCatTitle
                : copy.editCatTitle
          }
          formSubtitle=""
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSaveForm()}
        >
          <div className="form-grid-2">
            {formTarget === 'consumer' && (
              <FormField
                id="m03-category"
                label={copy.categoryLabel}
                required
                error={fieldErrors.categoryId}
              >
                <SelectInput
                  id="m03-category"
                  value={formCategoryId}
                  hasError={Boolean(fieldErrors.categoryId)}
                  onChange={(e) => {
                    setFormCategoryId(e.target.value);
                    setFieldErrors((prev) => ({ ...prev, categoryId: '' }));
                  }}
                >
                  <option value="">{copy.categoryPlaceholder}</option>
                  {allCategories
                    .filter((c) => c.isActive || c.id === formCategoryId)
                    .map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                      </option>
                    ))}
                </SelectInput>
              </FormField>
            )}

            <FormField id="m03-name" label={copy.nameLabel} required error={fieldErrors.name}>
              <TextInput
                ref={nameRef}
                id="m03-name"
                value={formName}
                hasError={Boolean(fieldErrors.name)}
                onChange={(e) => {
                  setFormName(e.target.value);
                  setFieldErrors((prev) => ({ ...prev, name: '' }));
                }}
              />
            </FormField>

            {mode === 'edit' && (
              <div className="form-field" style={{ justifyContent: 'center' }}>
                <label className="matrix-inline-check">
                  <input
                    type="checkbox"
                    className="matrix-checkbox"
                    checked={formActive}
                    onChange={(e) => setFormActive(e.target.checked)}
                  />
                  <span>{copy.activeLabel}</span>
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
        cancelLabel={t.modalCancelBtn}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={() => {
          const active = modal;
          if (!active) return;
          if (active.variant === 'confirm' && active.onConfirmAction) {
            void active.onConfirmAction();
            return;
          }
          setModal(null);
          if (active.variant === 'error' && fieldErrors.name) {
            nameRef.current?.focus();
          }
        }}
        onCancel={() => setModal(null)}
      />
    </>
  );
}
