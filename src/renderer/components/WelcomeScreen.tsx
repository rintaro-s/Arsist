import React from 'react';
import { Plus, FolderOpen, FileText, Glasses, Code, Layout, Box } from 'lucide-react';

interface WelcomeScreenProps {
  onNewProject: () => void;
}

export function WelcomeScreen({ onNewProject }: WelcomeScreenProps) {
  const handleOpenProject = async () => {
    if (!window.electronAPI) return;
    const path = await window.electronAPI.fs.selectDirectory();
    if (path) {
      await window.electronAPI.project.load(path);
    }
  };

  return (
    <div className="flex-1 flex items-center justify-center bg-arsist-bg">
      <div className="max-w-2xl w-full px-8">
        {/* Logo and Title */}
        <div className="text-center mb-10">
          <div className="w-20 h-20 bg-arsist-accent rounded-2xl flex items-center justify-center mx-auto mb-4">
            <Glasses size={40} className="text-arsist-bg" />
          </div>
          <h1 className="text-3xl font-bold text-arsist-text mb-2">Arsist Engine</h1>
          <p className="text-arsist-muted">
            ARグラス・クロスプラットフォーム開発エンジン
          </p>
        </div>

        {/* Action Cards */}
        <div className="grid grid-cols-2 gap-4 mb-6">
          <button
            onClick={onNewProject}
            className="p-5 bg-arsist-surface rounded-lg border border-arsist-border hover:border-arsist-accent transition-all text-left group"
          >
            <div className="w-10 h-10 bg-arsist-accent/20 rounded-lg flex items-center justify-center mb-3 group-hover:bg-arsist-accent/30 transition-colors">
              <Plus size={20} className="text-arsist-accent" />
            </div>
            <h3 className="font-medium mb-1">新規プロジェクト</h3>
            <p className="text-xs text-arsist-muted">
              テンプレートから新しいARアプリを作成
            </p>
          </button>

          <button
            onClick={handleOpenProject}
            className="p-5 bg-arsist-surface rounded-lg border border-arsist-border hover:border-arsist-accent transition-all text-left group"
          >
            <div className="w-10 h-10 bg-arsist-hover rounded-lg flex items-center justify-center mb-3 group-hover:bg-arsist-active transition-colors">
              <FolderOpen size={20} className="text-arsist-muted" />
            </div>
            <h3 className="font-medium mb-1">プロジェクトを開く</h3>
            <p className="text-xs text-arsist-muted">
              既存のArsistプロジェクトを読み込む
            </p>
          </button>
        </div>

        {/* Features */}
        <div className="grid grid-cols-3 gap-3 mb-6">
          <div className="p-3 bg-arsist-surface/50 rounded-lg border border-arsist-border text-center">
            <Box size={20} className="text-arsist-primary mx-auto mb-1" />
            <p className="text-xs text-arsist-muted">3Dシーン編集</p>
          </div>
          <div className="p-3 bg-arsist-surface/50 rounded-lg border border-arsist-border text-center">
            <Layout size={20} className="text-arsist-warning mx-auto mb-1" />
            <p className="text-xs text-arsist-muted">UI/HUDデザイン</p>
          </div>
          <div className="p-3 bg-arsist-surface/50 rounded-lg border border-arsist-border text-center">
            <Code size={20} className="text-arsist-accent mx-auto mb-1" />
            <p className="text-xs text-arsist-muted">コードモード</p>
          </div>
        </div>

        {/* Supported Devices */}
        <div className="bg-arsist-surface/50 rounded-lg p-4 border border-arsist-border">
          <h4 className="text-xs font-medium text-arsist-muted mb-3">対応デバイス</h4>
          <div className="flex flex-wrap gap-2">
            {['XREAL One', 'XREAL Air 2', 'Rokid Max', 'VITURE One'].map((device) => (
              <span
                key={device}
                className="px-2 py-1 bg-arsist-hover rounded text-xs"
              >
                {device}
              </span>
            ))}
            <span className="px-2 py-1 bg-arsist-bg rounded text-xs text-arsist-muted">
              + 追加予定
            </span>
          </div>
        </div>

        {/* Quick Start Hint */}
        <div className="mt-4 text-center">
          <p className="text-xs text-arsist-muted">
            <span className="kbd">Ctrl+N</span> 新規作成 | <span className="kbd">Ctrl+O</span> 開く | <span className="kbd">Ctrl+,</span> 設定
          </p>
        </div>
      </div>
    </div>
  );
}
