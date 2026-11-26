import { AppLanguage } from '../../constants/uiBaseline';
import { PermissionsEditorPage } from '../permissions/PermissionsEditorPage';

export interface UserPermissionsMatrixPageProps {
  userId: string;
  lang: AppLanguage;
  canManage: boolean;
  actorPermissions?: string[];
  onBack: () => void;
  onSessionExpired: () => void;
  onPermissionsChanged?: () => void;
}

export function UserPermissionsMatrixPage({ userId, canManage, actorPermissions, ...props }: UserPermissionsMatrixPageProps) {
  return <PermissionsEditorPage {...props} kind="users" subjectId={userId}
    actorPermissions={actorPermissions ?? ['permissions.view', ...(canManage ? ['permissions.manage'] : [])]} />;
}
