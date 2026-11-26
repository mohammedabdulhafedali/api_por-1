export const DOCUMENT_GROUPS = [
  { id: 'document', ar: 'بيانات الوثيقة', en: 'Document details', codes: ['doc.view', 'doc.edit', 'doc.delete', 'doc.access.manage'] },
  { id: 'integration', ar: 'التكامل والمطورون', en: 'Integration & developers', codes: ['doc.manage_integration', 'doc.assign'] },
  { id: 'files', ar: 'الملفات', en: 'Files', codes: ['doc.files.view', 'doc.files.download', 'doc.files.upload', 'doc.files.delete'] },
  { id: 'processing', ar: 'معالجة الملفات', en: 'File processing', codes: ['doc.files.rescan', 'doc.files.word_generate'] },
  { id: 'versions', ar: 'الإصدارات والمراجعات', en: 'Versions & revisions', codes: ['doc.versions.create', 'doc.versions.usage', 'doc.revisions.create', 'doc.revisions.submit'] },
  { id: 'changes', ar: 'طلبات التغيير', en: 'Change requests', codes: ['doc.changes.create', 'doc.changes.edit', 'doc.changes.submit'] },
  { id: 'review', ar: 'الاعتماد', en: 'Approval', codes: ['doc.review.approve', 'doc.review.return', 'doc.review.reject'] },
  { id: 'publication', ar: 'النشر', en: 'Publishing', codes: ['doc.publications.create', 'doc.publications.withdraw'] },
];
