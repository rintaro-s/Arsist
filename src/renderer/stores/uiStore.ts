/**
 * Arsist Engine - UI Store
 * UI状態管理 (Zustand)
 */
import { create } from 'zustand';

type ViewType = 'scene' | 'ui' | 'logic' | 'code';
type UIEditMode = 'visual' | 'code';

interface UIState {
  // ビュー
  currentView: ViewType;
  setCurrentView: (view: ViewType) => void;
  
  // UI編集モード（ビジュアル/コード）
  uiEditMode: UIEditMode;
  setUIEditMode: (mode: UIEditMode) => void;
  
  // ダイアログ
  showNewProjectDialog: boolean;
  showBuildDialog: boolean;
  showSettingsDialog: boolean;
  setShowNewProjectDialog: (show: boolean) => void;
  setShowBuildDialog: (show: boolean) => void;
  setShowSettingsDialog: (show: boolean) => void;
  
  // パネル
  leftPanelWidth: number;
  rightPanelWidth: number;
  bottomPanelHeight: number;
  setLeftPanelWidth: (width: number) => void;
  setRightPanelWidth: (width: number) => void;
  setBottomPanelHeight: (height: number) => void;
  
  // 3Dビューポート
  showGrid: boolean;
  showAxes: boolean;
  snapToGrid: boolean;
  gridSize: number;
  setShowGrid: (show: boolean) => void;
  setShowAxes: (show: boolean) => void;
  setSnapToGrid: (snap: boolean) => void;
  setGridSize: (size: number) => void;
  
  // トランスフォームモード
  transformMode: 'translate' | 'rotate' | 'scale';
  transformSpace: 'local' | 'world';
  setTransformMode: (mode: 'translate' | 'rotate' | 'scale') => void;
  setTransformSpace: (space: 'local' | 'world') => void;
  
  // ビルド状態
  buildProgress: number;
  buildMessage: string;
  buildLogs: string[];
  isBuilding: boolean;
  setBuildProgress: (progress: number, message: string) => void;
  addBuildLog: (log: string) => void;
  clearBuildLogs: () => void;
  setIsBuilding: (building: boolean) => void;
  
  // コンテキストメニュー
  contextMenu: {
    show: boolean;
    x: number;
    y: number;
    items: ContextMenuItem[];
  };
  showContextMenu: (x: number, y: number, items: ContextMenuItem[]) => void;
  hideContextMenu: () => void;
  
  // 通知
  notifications: Notification[];
  addNotification: (notification: Omit<Notification, 'id'>) => void;
  removeNotification: (id: string) => void;
}

interface ContextMenuItem {
  label: string;
  icon?: string;
  action?: () => void;
  shortcut?: string;
  separator?: boolean;
  disabled?: boolean;
  submenu?: ContextMenuItem[];
}

interface Notification {
  id: string;
  type: 'info' | 'success' | 'warning' | 'error';
  message: string;
  duration?: number;
}

export const useUIStore = create<UIState>((set, get) => ({
  // ビュー
  currentView: 'scene',
  setCurrentView: (view) => set({ currentView: view }),
  
  // UI編集モード
  uiEditMode: 'visual',
  setUIEditMode: (mode) => set({ uiEditMode: mode }),
  
  // ダイアログ
  showNewProjectDialog: false,
  showBuildDialog: false,
  showSettingsDialog: false,
  setShowNewProjectDialog: (show) => set({ showNewProjectDialog: show }),
  setShowBuildDialog: (show) => set({ showBuildDialog: show }),
  setShowSettingsDialog: (show) => set({ showSettingsDialog: show }),
  
  // パネル
  leftPanelWidth: 280,
  rightPanelWidth: 320,
  bottomPanelHeight: 200,
  setLeftPanelWidth: (width) => set({ leftPanelWidth: Math.max(200, Math.min(500, width)) }),
  setRightPanelWidth: (width) => set({ rightPanelWidth: Math.max(250, Math.min(500, width)) }),
  setBottomPanelHeight: (height) => set({ bottomPanelHeight: Math.max(100, Math.min(400, height)) }),
  
  // 3Dビューポート
  showGrid: true,
  showAxes: true,
  snapToGrid: false,
  gridSize: 0.25,
  setShowGrid: (show) => set({ showGrid: show }),
  setShowAxes: (show) => set({ showAxes: show }),
  setSnapToGrid: (snap) => set({ snapToGrid: snap }),
  setGridSize: (size) => set({ gridSize: size }),
  
  // トランスフォームモード
  transformMode: 'translate',
  transformSpace: 'world',
  setTransformMode: (mode) => set({ transformMode: mode }),
  setTransformSpace: (space) => set({ transformSpace: space }),
  
  // ビルド状態
  buildProgress: 0,
  buildMessage: '',
  buildLogs: [],
  isBuilding: false,
  setBuildProgress: (progress, message) => set({ buildProgress: progress, buildMessage: message }),
  addBuildLog: (log) => set((state) => ({ buildLogs: [...state.buildLogs, log] })),
  clearBuildLogs: () => set({ buildLogs: [], buildProgress: 0, buildMessage: '' }),
  setIsBuilding: (building) => set({ isBuilding: building }),
  
  // コンテキストメニュー
  contextMenu: { show: false, x: 0, y: 0, items: [] },
  showContextMenu: (x, y, items) => set({ contextMenu: { show: true, x, y, items } }),
  hideContextMenu: () => set({ contextMenu: { show: false, x: 0, y: 0, items: [] } }),
  
  // 通知
  notifications: [],
  addNotification: (notification) => {
    const id = Math.random().toString(36).substr(2, 9);
    set((state) => ({
      notifications: [...state.notifications, { ...notification, id }],
    }));
    
    // 自動削除
    if (notification.duration !== 0) {
      setTimeout(() => {
        get().removeNotification(id);
      }, notification.duration || 5000);
    }
  },
  removeNotification: (id) => set((state) => ({
    notifications: state.notifications.filter((n) => n.id !== id),
  })),
}));
