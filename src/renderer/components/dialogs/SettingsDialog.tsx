import React, { useEffect, useState } from 'react';
import { X, FolderOpen } from 'lucide-react';
import { useUIStore } from '../../stores/uiStore';

interface SettingsDialogProps {
  onClose: () => void;
}

export function SettingsDialog({ onClose }: SettingsDialogProps) {
  const {
    leftPanelWidth,
    rightPanelWidth,
    bottomPanelHeight,
    setLeftPanelWidth,
    setRightPanelWidth,
    setBottomPanelHeight,
    addNotification,
  } = useUIStore();

  const [unityPath, setUnityPath] = useState('');
  const [unityVersion, setUnityVersion] = useState('');
  const [defaultOutputPath, setDefaultOutputPath] = useState('');
  const [versionDetected, setVersionDetected] = useState<string | null>(null);
  const [unityCandidates, setUnityCandidates] = useState<string[]>([]);
  const [detectingUnity, setDetectingUnity] = useState(false);

  useEffect(() => {
    const loadSettings = async () => {
      if (!window.electronAPI) return;

      const storedUnityPath = await window.electronAPI.unity.getPath();
      if (storedUnityPath) {
        setUnityPath(storedUnityPath);
      }

      const storedUnityVersion = await window.electronAPI.store.get('unityVersion');
      if (storedUnityVersion) {
        setUnityVersion(storedUnityVersion);
      }

      const storedOutputPath = await window.electronAPI.store.get('defaultOutputPath');
      if (storedOutputPath) {
        setDefaultOutputPath(storedOutputPath);
      }

      const validation = await window.electronAPI.unity.validate();
      if (validation?.version) {
        setVersionDetected(validation.version);
      }
    };

    loadSettings();
  }, []);

  const handleSelectUnityPath = async () => {
    if (!window.electronAPI) return;

    const path = await window.electronAPI.fs.selectFile([
      { name: 'Unity', extensions: ['exe', 'app', ''] },
    ]);

    if (path) {
      setUnityPath(path);
      await window.electronAPI.unity.setPath(path);
      const validation = await window.electronAPI.unity.validate();
      if (validation?.version) {
        setVersionDetected(validation.version);
      }
    }
  };

  const handleDetectUnityPath = async () => {
    if (!window.electronAPI) return;
    setDetectingUnity(true);
    try {
      const result = await window.electronAPI.unity.detectPaths();
      if (!result?.success) {
        addNotification({ type: 'error', message: result?.error || 'Unityパスの自動検出に失敗しました' });
        return;
      }
      setUnityCandidates(result.candidates || []);
      if ((result.candidates || []).length > 0) {
        const p = result.candidates[0];
        setUnityPath(p);
        await window.electronAPI.unity.setPath(p);
        const validation = await window.electronAPI.unity.validate();
        if (validation?.version) {
          setVersionDetected(validation.version);
        }
      } else {
        addNotification({ type: 'warning', message: 'Unityが見つかりませんでした。Unity HubのEditorインストールを確認してください。' });
      }
    } finally {
      setDetectingUnity(false);
    }
  };

  const handleSelectOutputPath = async () => {
    if (!window.electronAPI) return;

    const path = await window.electronAPI.fs.selectDirectory();
    if (path) {
      setDefaultOutputPath(path);
      await window.electronAPI.store.set('defaultOutputPath', path);
    }
  };

  const handleSave = async () => {
    if (!window.electronAPI) return;

    if (unityPath) {
      await window.electronAPI.unity.setPath(unityPath);
    }
    await window.electronAPI.store.set('unityVersion', unityVersion.trim());
    await window.electronAPI.store.set('defaultOutputPath', defaultOutputPath);
    await window.electronAPI.store.set('layoutSettings', {
      leftPanelWidth,
      rightPanelWidth,
      bottomPanelHeight,
    });

    addNotification({ type: 'success', message: '設定を保存しました' });
    onClose();
  };

  const handleResetLayout = () => {
    setLeftPanelWidth(280);
    setRightPanelWidth(320);
    setBottomPanelHeight(200);
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal max-w-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header flex items-center justify-between">
          <span>設定</span>
          <button onClick={onClose} className="btn-icon">
            <X size={18} />
          </button>
        </div>

        <div className="modal-body space-y-6">
          <section>
            <h3 className="text-xs font-medium text-arsist-accent mb-3">Unity設定</h3>
            <div className="space-y-3">
              <div>
                <label className="input-label">Unity パス</label>
                <div className="flex gap-2">
                  <input
                    type="text"
                    value={unityPath}
                    onChange={(e) => setUnityPath(e.target.value)}
                    className="input flex-1"
                    placeholder="Linux: /home/<user>/Unity/Hub/Editor/<ver>/Editor/Unity"
                  />
                  <button onClick={handleSelectUnityPath} className="btn btn-secondary">
                    <FolderOpen size={16} />
                  </button>
                  <button onClick={handleDetectUnityPath} className="btn btn-secondary" disabled={detectingUnity}>
                    自動検出
                  </button>
                </div>
                {versionDetected && (
                  <p className="text-xs text-arsist-success mt-1">検出: {versionDetected}</p>
                )}
                <p className="text-xs text-arsist-muted mt-1">
                  Linuxは通常 <span className="font-mono">.../Editor/Unity</span>、Windowsは <span className="font-mono">.../Editor/Unity.exe</span> を指定します
                </p>

                {unityCandidates.length > 0 && (
                  <div className="mt-2 p-2 bg-arsist-bg border border-arsist-border rounded">
                    <div className="text-[10px] text-arsist-muted mb-1">検出候補（クリックで設定）</div>
                    <div className="space-y-1">
                      {unityCandidates.map((p) => (
                        <button
                          key={p}
                          className="w-full text-left text-[10px] text-arsist-text hover:text-arsist-accent truncate"
                          onClick={async () => {
                            setUnityPath(p);
                            await window.electronAPI.unity.setPath(p);
                            const validation = await window.electronAPI.unity.validate();
                            if (validation?.version) setVersionDetected(validation.version);
                          }}
                        >
                          {p}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>

              <div>
                <label className="input-label">必要なUnityバージョン</label>
                <input
                  type="text"
                  value={unityVersion}
                  onChange={(e) => setUnityVersion(e.target.value)}
                  className="input"
                  placeholder="2022.3.20f1"
                />
                <p className="text-xs text-arsist-muted mt-1">
                  指定したバージョン以上でビルドが実行されます
                </p>
              </div>
            </div>
          </section>

          <section>
            <h3 className="text-xs font-medium text-arsist-primary mb-3">ビルド設定</h3>
            <div>
              <label className="input-label">既定の出力先</label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={defaultOutputPath}
                  onChange={(e) => setDefaultOutputPath(e.target.value)}
                  className="input flex-1"
                  placeholder="/path/to/output"
                />
                <button onClick={handleSelectOutputPath} className="btn btn-secondary">
                  <FolderOpen size={16} />
                </button>
              </div>
            </div>
          </section>

          <section>
            <h3 className="text-xs font-medium text-arsist-warning mb-3">レイアウト設定</h3>
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="input-label">左パネル幅</label>
                <input
                  type="number"
                  value={leftPanelWidth}
                  onChange={(e) => setLeftPanelWidth(Number(e.target.value))}
                  className="input"
                  min={200}
                  max={500}
                />
              </div>
              <div>
                <label className="input-label">右パネル幅</label>
                <input
                  type="number"
                  value={rightPanelWidth}
                  onChange={(e) => setRightPanelWidth(Number(e.target.value))}
                  className="input"
                  min={250}
                  max={500}
                />
              </div>
              <div>
                <label className="input-label">下パネル高さ</label>
                <input
                  type="number"
                  value={bottomPanelHeight}
                  onChange={(e) => setBottomPanelHeight(Number(e.target.value))}
                  className="input"
                  min={100}
                  max={400}
                />
              </div>
            </div>
            <div className="mt-3">
              <button onClick={handleResetLayout} className="btn btn-ghost text-xs">
                レイアウトをリセット
              </button>
            </div>
          </section>
        </div>

        <div className="modal-footer flex justify-end gap-2">
          <button onClick={onClose} className="btn btn-ghost">キャンセル</button>
          <button onClick={handleSave} className="btn btn-success">保存</button>
        </div>
      </div>
    </div>
  );
}
