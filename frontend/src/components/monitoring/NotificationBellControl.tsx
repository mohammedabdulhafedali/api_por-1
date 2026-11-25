import { useCallback, useEffect, useState } from 'react';
import {
  getMyNotifications,
  markNotificationAsRead,
  NotificationItem,
} from '../../api/changesAndNotificationsApi';
import { AppLanguage } from '../../constants/uiBaseline';
import { Button, ReferenceIcon, StatusBadge } from '../ui';

interface NotificationBellControlProps {
  lang: AppLanguage;
  enabled: boolean;
  onNavigateToScreen: (navId: string) => void;
}

export function NotificationBellControl({
  lang,
  enabled,
  onNavigateToScreen,
}: NotificationBellControlProps) {
  const [open, setOpen] = useState(false);
  const [unreadCount, setUnreadCount] = useState(0);
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(false);

  const loadNotifications = useCallback(async () => {
    if (!enabled) return;
    setLoading(true);
    try {
      const res = await getMyNotifications(false, lang);
      setUnreadCount(res.unreadCount);
      setItems(res.items);
    } catch {
      // Non-fatal in header poll
    } finally {
      setLoading(false);
    }
  }, [enabled, lang]);

  useEffect(() => {
    void loadNotifications();
  }, [loadNotifications]);

  const handleMarkRead = async (id: string) => {
    try {
      const updated = await markNotificationAsRead(id, lang);
      setItems((prev) => prev.map((it) => (it.id === id ? updated : it)));
      setUnreadCount((c) => Math.max(0, c - 1));
    } catch {
      // Ignore transient error
    }
  };

  return (
    <div className="notification-bell-wrapper">
      <button
        type="button"
        className="topbar-bell-btn icon-btn"
        data-testid="topbar-notifications-bell"
        aria-label={lang === 'ar' ? 'مركز الإشعارات' : 'Notifications Center'}
        onClick={() => {
          const next = !open;
          setOpen(next);
          if (next) void loadNotifications();
        }}
      >
        <ReferenceIcon name="bell" size={17} />
        {unreadCount > 0 && (
          <span className="topbar-bell-badge counter" data-testid="notifications-unread-badge">
            {unreadCount}
          </span>
        )}
      </button>

      {open && (
        <div
          className="notification-popover-panel"
          role="dialog"
          aria-label={lang === 'ar' ? 'الإشعارات الداخلية' : 'Internal Notifications'}
          data-testid="notifications-popover"
        >
          <div className="notification-popover-header">
            <strong>{lang === 'ar' ? 'الإشعارات الداخلية (UI-20)' : 'Notifications (UI-20)'}</strong>
            <Button variant="outline" size="sm" onClick={() => setOpen(false)}>
              {lang === 'ar' ? 'إغلاق' : 'Close'}
            </Button>
          </div>

          <div className="notification-popover-body">
            {loading && items.length === 0 ? (
              <p className="section-subtitle">
                {lang === 'ar' ? 'جارٍ التحديث...' : 'Loading...'}
              </p>
            ) : items.length === 0 ? (
              <p className="section-subtitle">
                {lang === 'ar' ? 'لا توجد إشعارات حاليًا.' : 'No notifications yet.'}
              </p>
            ) : (
              items.slice(0, 10).map((item) => (
                <div
                  key={item.id}
                  className={`notification-popover-item${item.isRead ? '' : ' unread'}`}
                >
                  <div className="notification-item-meta">
                    <StatusBadge variant={item.isRead ? 'neutral' : 'info'}>
                      {item.kind}
                    </StatusBadge>
                    <small>{new Date(item.createdAt).toLocaleString()}</small>
                  </div>
                  <p className="notification-item-text">
                    {lang === 'ar' ? item.summaryAr : item.summaryEn}
                  </p>
                  <div className="notification-item-actions">
                    {!item.isRead && (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => void handleMarkRead(item.id)}
                      >
                        {lang === 'ar' ? 'تعليم كمقروء' : 'Mark Read'}
                      </Button>
                    )}
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        setOpen(false);
                        onNavigateToScreen('UI-22');
                      }}
                    >
                      {lang === 'ar' ? 'فتح التكاملات' : 'Open Integrations'}
                    </Button>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}

