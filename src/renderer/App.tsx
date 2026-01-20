import { useEffect } from 'react';
import { TitleBar } from './components/TitleBar';
import { MainLayout } from './components/MainLayout';
import { WelcomeScreen } from './components/WelcomeScreen';
import { BuildDialog } from './components/dialogs/BuildDialog';
import { NewProjectDialog } from './components/dialogs/NewProjectDialog';
import { SettingsDialog } from './components/dialogs/SettingsDialog';
import { useProjectStore } from './stores/projectStore';
import { useUIStore } from './stores/uiStore';

declare global {
  interface Window {
    electronAPI: {
      project: {
        create: (options: any) => Promise<any>;
        load: (path: string) => Promise<any>;
        save: (data: any) => Promise<any>;
        export: (options: any) => Promise<any>;
      };
      unity: {
        setPath: (path: string) => Promise<any>;
        getPath: () => Promise<string>;
        build: (config: any) => Promise<any>;
        validate: () => Promise<any>;
        detectPaths: () => Promise<{ success: boolean; candidates: string[]; error?: string }>;
        onBuildProgress: (callback: (progress: any) => void) => void;
        onBuildLog: (callback: (log: string) => void) => void;
      };
      adapters: {
        list: () => Promise<any[]>;
        get: (id: string) => Promise<any>;
        applyPatch: (id: string, path: string) => Promise<any>;
      };
      fs: {
        readFile: (path: string) => Promise<any>;
        writeFile: (path: string, content: string) => Promise<any>;
        selectDirectory: () => Promise<string | null>;
        selectFile: (filters?: any[]) => Promise<string | null>;
      };
      assets: {
        import: (params: {
          projectPath: string;
          sourcePath: string;
          kind?: 'model' | 'texture' | 'video' | 'other';
        }) => Promise<{ success: boolean; assetPath?: string; error?: string }>;
        list: (params: { projectPath: string }) => Promise<{ success: boolean; items: any[]; error?: string }>;
      };
      store: {
        get: (key: string) => Promise<any>;
        set: (key: string, value: any) => Promise<any>;
      };
      window: {
        minimize: () => void;
        maximize: () => void;
        close: () => void;
      };
      menu: {
        onNewProject: (callback: () => void) => void;
        onSave: (callback: () => void) => void;
        onSaveAs: (callback: () => void) => void;
        onBuildSettings: (callback: () => void) => void;
        onBuild: (callback: () => void) => void;
        onSettings: (callback: () => void) => void;
        onDelete: (callback: () => void) => void;
        onViewChange: (callback: (view: string) => void) => void;
        onProjectOpen: (callback: (path: string) => void) => void;
      };
    };
  }
}

export default function App() {
  const { project, loadProject, saveProject } = useProjectStore();
  const { 
    showNewProjectDialog, 
    showBuildDialog,
    showSettingsDialog,
    setShowNewProjectDialog,
    setShowBuildDialog,
    setShowSettingsDialog,
    setCurrentView 
  } = useUIStore();

  useEffect(() => {
    if (!project) return;
    if (project.appType === '2D_HeadLocked') {
      setCurrentView('ui');
    } else {
      setCurrentView('scene');
    }
  }, [project?.id]);

  useEffect(() => {
    // メニューイベントのリスナー設定
    if (window.electronAPI) {
      window.electronAPI.menu.onNewProject(() => {
        setShowNewProjectDialog(true);
      });

      window.electronAPI.menu.onSave(() => {
        saveProject();
      });

      window.electronAPI.menu.onBuild(() => {
        setShowBuildDialog(true);
      });

      window.electronAPI.menu.onBuildSettings(() => {
        setShowBuildDialog(true);
      });

      window.electronAPI.menu.onSettings(() => {
        setShowSettingsDialog(true);
      });

      window.electronAPI.menu.onViewChange((view) => {
        if (view === '3d') setCurrentView('scene');
        else if (view === '2d') setCurrentView('ui');
        else if (view === 'logic') setCurrentView('scene');
      });

      window.electronAPI.menu.onProjectOpen(async (path) => {
        await loadProject(path);
      });
    }
  }, []);

  return (
    <div className="flex flex-col h-screen w-screen bg-arsist-bg">
      <TitleBar />
      
      {project ? (
        <MainLayout />
      ) : (
        <WelcomeScreen onNewProject={() => setShowNewProjectDialog(true)} />
      )}

      {/* Dialogs */}
      {showNewProjectDialog && (
        <NewProjectDialog onClose={() => setShowNewProjectDialog(false)} />
      )}
      
      {showBuildDialog && (
        <BuildDialog onClose={() => setShowBuildDialog(false)} />
      )}

      {showSettingsDialog && (
        <SettingsDialog onClose={() => setShowSettingsDialog(false)} />
      )}
    </div>
  );
}
