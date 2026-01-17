import React, { useState } from 'react';
import { Terminal, AlertCircle, FileText, Package, HelpCircle } from 'lucide-react';
import { useUIStore } from '../../stores/uiStore';
import { useProjectStore } from '../../stores/projectStore';

type TabType = 'console' | 'assets' | 'build';

export function BottomPanel() {
  const [activeTab, setActiveTab] = useState<TabType>('console');
  const { buildLogs, buildProgress, buildMessage, isBuilding } = useUIStore();

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* Tabs */}
      <div className="flex items-center border-b border-arsist-border bg-arsist-hover">
        <Tab
          icon={<Terminal size={14} />}
          label="コンソール"
          active={activeTab === 'console'}
          onClick={() => setActiveTab('console')}
        />
        <Tab
          icon={<Package size={14} />}
          label="アセット"
          active={activeTab === 'assets'}
          onClick={() => setActiveTab('assets')}
        />
        <Tab
          icon={<FileText size={14} />}
          label="ビルドログ"
          active={activeTab === 'build'}
          onClick={() => setActiveTab('build')}
          badge={isBuilding ? '...' : undefined}
        />

        {/* Build Progress */}
        {isBuilding && (
          <div className="ml-auto mr-4 flex items-center gap-2 text-sm">
            <div className="w-32 h-2 bg-arsist-bg rounded-full overflow-hidden">
              <div 
                className="h-full bg-arsist-accent transition-all duration-300"
                style={{ width: `${buildProgress}%` }}
              />
            </div>
            <span className="text-arsist-muted text-xs">{buildMessage}</span>
          </div>
        )}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-hidden">
        {activeTab === 'console' && <ConsolePanel />}
        {activeTab === 'assets' && <AssetsPanel />}
        {activeTab === 'build' && <BuildLogPanel logs={buildLogs} />}
      </div>
    </div>
  );
}

interface TabProps {
  icon: React.ReactNode;
  label: string;
  active: boolean;
  onClick: () => void;
  badge?: string;
}

function Tab({ icon, label, active, onClick, badge }: TabProps) {
  return (
    <button
      onClick={onClick}
      className={`flex items-center gap-2 px-4 py-2 text-xs border-b-2 transition-colors ${
        active
          ? 'border-arsist-accent text-arsist-text bg-arsist-surface'
          : 'border-transparent text-arsist-muted hover:text-arsist-text hover:bg-arsist-hover'
      }`}
    >
      {icon}
      {label}
      {badge && (
        <span className="px-1.5 py-0.5 bg-arsist-accent text-white text-[10px] rounded">
          {badge}
        </span>
      )}
    </button>
  );
}

function ConsolePanel() {
  const [logs] = useState<{ type: 'info' | 'warning' | 'error'; message: string; time: string }[]>([
    { type: 'info', message: 'Arsist Engine 起動完了', time: new Date().toLocaleTimeString() },
  ]);

  return (
    <div className="h-full overflow-y-auto p-2 font-mono text-xs">
      {logs.map((log, index) => (
        <div 
          key={index}
          className={`flex items-start gap-2 py-1 ${
            log.type === 'error' ? 'text-red-400' :
            log.type === 'warning' ? 'text-yellow-400' :
            'text-arsist-muted'
          }`}
        >
          <span className="text-arsist-muted">[{log.time}]</span>
          {log.type === 'error' && <AlertCircle size={12} className="mt-0.5" />}
          <span>{log.message}</span>
        </div>
      ))}
      {logs.length === 0 && (
        <div className="text-center py-4 text-arsist-muted">
          ログはありません
        </div>
      )}
    </div>
  );
}

function AssetsPanel() {
  const { project } = useProjectStore();

  return (
    <div className="h-full overflow-y-auto p-4">
      <div className="grid grid-cols-6 gap-4">
        {/* Asset folders */}
        {['Textures', 'Models', 'Fonts', 'Audio'].map(folder => (
          <div 
            key={folder}
            className="flex flex-col items-center gap-2 p-3 rounded-lg hover:bg-arsist-primary/30 cursor-pointer"
          >
            <div className="w-12 h-12 bg-arsist-hover rounded-lg flex items-center justify-center">
              <Package size={24} className="text-arsist-muted" />
            </div>
            <span className="text-xs text-arsist-muted">{folder}</span>
          </div>
        ))}
      </div>

      {!project && (
        <div className="text-center py-8 text-arsist-muted text-xs">
          プロジェクトを開いてください
        </div>
      )}
    </div>
  );
}

interface BuildLogPanelProps {
  logs: string[];
}

function BuildLogPanel({ logs }: BuildLogPanelProps) {
  return (
    <div className="h-full overflow-y-auto p-2 font-mono text-xs bg-arsist-bg">
      {logs.map((log, index) => (
        <div 
          key={index}
          className={`py-0.5 ${
            log.includes('Error') ? 'text-arsist-error' :
            log.includes('Warning') ? 'text-arsist-warning' :
            log.includes('[Arsist]') ? 'text-arsist-accent' :
            'text-arsist-muted'
          }`}
        >
          {log}
        </div>
      ))}
      {logs.length === 0 && (
        <div className="text-center py-4 text-arsist-muted text-xs">
          ビルドログはありません
        </div>
      )}
    </div>
  );
}
