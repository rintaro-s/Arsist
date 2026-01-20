import { useEffect, useMemo, useState } from 'react';
import { useProjectStore } from '../../stores/projectStore';
import { useUIStore } from '../../stores/uiStore';
import { 
  FileCode, 
  Palette, 
  Code2,
  RefreshCw,
  ChevronRight,
  Eye,
  Copy,
  Check
} from 'lucide-react';

type FileType = 'html' | 'css' | 'js';

// デフォルトのテンプレート
const DEFAULT_HTML = `<!-- Arsist UI - HTML -->
<!-- このファイルでUIの構造を定義します -->
<div class="hud-container" data-arsist-root="true" data-arsist-type="Panel">
  <header class="hud-header">
    <h1 class="title" data-arsist-type="Text">AR Application</h1>
  </header>
  
  <main class="hud-main">
    <!-- メインコンテンツ -->
    <div class="info-panel" data-arsist-type="Panel">
      <h2 data-arsist-type="Text">情報パネル</h2>
      <p data-arsist-type="Text">ここに動的なコンテンツを表示</p>
    </div>
  </main>
  
  <footer class="hud-footer">
    <button class="btn-action" data-arsist-type="Button" onclick="onAction()">アクション</button>
    <button class="btn-secondary" data-arsist-type="Button" onclick="onSettings()">設定</button>
  </footer>
</div>
`;

const DEFAULT_CSS = `/* Arsist UI - CSS */
/* ARグラス向けのスタイル定義 */

/* ベース設定 */
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

/* HUDコンテナ */
.hud-container {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  font-family: 'Inter', system-ui, sans-serif;
  color: #ffffff;
  background: transparent;
}

/* ヘッダー */
.hud-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 40px;
  background: rgba(0, 0, 0, 0.4);
  backdrop-filter: blur(10px);
}

.title {
  font-size: 24px;
  font-weight: 600;
  letter-spacing: 0.5px;
}

/* メインエリア */
.hud-main {
  flex: 1;
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 40px;
}

.info-panel {
  background: rgba(0, 0, 0, 0.3);
  backdrop-filter: blur(8px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  padding: 24px;
  min-width: 300px;
}

.info-panel h2 {
  font-size: 18px;
  margin-bottom: 12px;
  color: #4ec9b0;
}

.info-panel p {
  font-size: 14px;
  color: rgba(255, 255, 255, 0.8);
}

/* フッター */
.hud-footer {
  display: flex;
  justify-content: center;
  gap: 16px;
  padding: 20px 40px;
  background: rgba(0, 0, 0, 0.4);
  backdrop-filter: blur(10px);
}

/* ボタン */
.btn-action {
  background: #4ec9b0;
  color: #1e1e1e;
  border: none;
  padding: 12px 24px;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-action:hover {
  background: #3db89f;
  transform: translateY(-2px);
}

.btn-secondary {
  background: rgba(255, 255, 255, 0.1);
  color: #ffffff;
  border: 1px solid rgba(255, 255, 255, 0.2);
  padding: 12px 24px;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-secondary:hover {
  background: rgba(255, 255, 255, 0.2);
}
`;

const DEFAULT_JS = `// Arsist UI - JavaScript
// UIのロジックを定義します

// 初期化
function init() {
  console.log('AR UI initialized');
}

// アクションボタン
function onAction() {
  console.log('Action button clicked');
  // カスタムアクションを実装
  
  // 例: Unity/Arsist Engineにイベント送信
  if (window.ArsistBridge) {
    window.ArsistBridge.sendEvent('action', { type: 'click' });
  }
}

// 設定ボタン
function onSettings() {
  console.log('Settings button clicked');
  // 設定画面を表示
}

// データバインディング用ヘルパー
function bindData(selector, value) {
  const el = document.querySelector(selector);
  if (el) {
    el.textContent = value;
  }
}

// Arsist Engine からのデータ受信
window.onArsistData = function(data) {
  console.log('Received data from engine:', data);
  // データに基づいてUIを更新
};

// 初期化実行
document.addEventListener('DOMContentLoaded', init);
`;

export function CodeEditor() {
  const { project, setUICode, syncUIFromCode, syncCodeFromUI } = useProjectStore();
  const { addNotification, addConsoleLog } = useUIStore();
  const [activeFile, setActiveFile] = useState<FileType>('html');
  const [showPreview, setShowPreview] = useState(true);
  const [copied, setCopied] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  if (!project) {
    return (
      <div className="w-full h-full flex items-center justify-center text-arsist-muted text-sm">
        プロジェクトを開いてください
      </div>
    );
  }

  const uiCode = project.uiCode || {
    html: DEFAULT_HTML,
    css: DEFAULT_CSS,
    js: DEFAULT_JS,
  };

  const activeContent = activeFile === 'html'
    ? uiCode.html
    : activeFile === 'css'
      ? uiCode.css
      : uiCode.js;

  const handleContentChange = (content: string) => {
    const result = setUICode(activeFile, content);
    if (!result.success) {
      addNotification({ type: 'error', message: `GUI同期に失敗しました: ${result.error}` });
    }
  };

  const handleCopy = () => {
    navigator.clipboard.writeText(activeContent);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const getFileIcon = (type: FileType) => {
    switch (type) {
      case 'html': return <FileCode size={14} className="text-orange-400" />;
      case 'css': return <Palette size={14} className="text-blue-400" />;
      case 'js': return <Code2 size={14} className="text-yellow-400" />;
    }
  };

  useEffect(() => {
    const onMessage = (ev: MessageEvent) => {
      const data = ev.data;
      if (!data || typeof data !== 'object') return;

      if (data?.type === 'arsist-preview:error') {
        const msg = typeof data.message === 'string' ? data.message : 'Unknown error';
        const details = typeof data.details === 'string' ? data.details : '';
        const formatted = details ? `${msg}\n${details}` : msg;
        setPreviewError(formatted);
        addConsoleLog({
          type: 'error',
          message: `[Preview] ${formatted}`,
        });
      }

      if (data?.type === 'arsist-preview:ready') {
        setPreviewError(null);
      }

      if (data?.type === 'arsist-bridge:event') {
        const name = typeof data.name === 'string' ? data.name : 'unknown';
        const payload = data.payload;
        addConsoleLog({
          type: 'info',
          message: `[ArsistBridge.sendEvent] ${name} ${payload ? JSON.stringify(payload) : ''}`,
        });
      }
    };

    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [addConsoleLog]);

  const previewHtml = useMemo(() => {
    const html = uiCode.html || '';
    const css = uiCode.css || '';
    const js = uiCode.js || '';

    // NOTE:
    // - エディタ側では「HTML断片」を入力として扱う。
    // - プレビュー/Unity WebView実行用に <html> 等でラップする。
    // - JSは new Function で実行し、構文エラーもcatchして表示する。

    const cssSafe = css;
    const htmlSafe = html;

    return `<!DOCTYPE html>
<html>
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <style>
      html, body { height: 100%; }
      body { margin: 0; background: #1e1e1e; overflow: hidden; }
      ${cssSafe}
    </style>
  </head>
  <body>
    <div id="__arsist_mount__">${htmlSafe}</div>

    <script>
      (function () {
        function post(type, payload) {
          try { parent.postMessage(Object.assign({ type: type }, payload || {}), '*'); } catch (_) {}
        }

        function showErrorOverlay(text) {
          try {
            var el = document.getElementById('__arsist_preview_error__');
            if (!el) {
              el = document.createElement('pre');
              el.id = '__arsist_preview_error__';
              el.style.position = 'absolute';
              el.style.left = '12px';
              el.style.top = '12px';
              el.style.right = '12px';
              el.style.maxHeight = '45%';
              el.style.overflow = 'auto';
              el.style.padding = '12px';
              el.style.background = 'rgba(0,0,0,0.85)';
              el.style.border = '1px solid rgba(255,255,255,0.15)';
              el.style.borderRadius = '8px';
              el.style.color = '#ff6b6b';
              el.style.fontSize = '12px';
              el.style.whiteSpace = 'pre-wrap';
              el.style.zIndex = '9999';
              document.body.appendChild(el);
            }
            el.textContent = 'プレビューできません\\n\\n' + text;
          } catch (_) {}
        }

        // preview用 ArsistBridge（Unity実機ではネイティブ側が注入する想定）
        if (!window.ArsistBridge) {
          var memory = {};
          window.ArsistBridge = {
            sendEvent: function (name, data) {
              post('arsist-bridge:event', { name: name, payload: data });
            },
            getData: function (key) {
              return memory[key];
            },
            setData: function (key, value) {
              memory[key] = value;
            },
            __simulateIncoming: function (data) {
              if (typeof window.onArsistData === 'function') {
                window.onArsistData(data);
              }
            }
          };
        }

        window.addEventListener('error', function (e) {
          var msg = (e && e.message) ? String(e.message) : 'Unknown error';
          var details = '';
          if (e && e.filename) details += e.filename;
          if (e && typeof e.lineno === 'number') details += ':' + e.lineno;
          if (e && typeof e.colno === 'number') details += ':' + e.colno;
          post('arsist-preview:error', { message: msg, details: details });
          showErrorOverlay(msg + (details ? ('\\n' + details) : ''));
        }, true);

        window.addEventListener('unhandledrejection', function (e) {
          var reason = e && e.reason ? e.reason : 'Unknown rejection';
          var msg = (reason && reason.message) ? reason.message : String(reason);
          post('arsist-preview:error', { message: 'UnhandledPromiseRejection', details: msg });
          showErrorOverlay('UnhandledPromiseRejection\\n' + msg);
        });

        // ユーザーJS実行（構文エラーも捕捉）
        try {
          var userJs = "\\n" + ${JSON.stringify(js)} + "\\n";
          (new Function(userJs))();
        } catch (err) {
          var msg = err && err.message ? err.message : String(err);
          post('arsist-preview:error', { message: 'JavaScript error', details: msg });
          showErrorOverlay('JavaScript error\\n' + msg);
        }

        post('arsist-preview:ready', {});
      })();
    </script>
  </body>
</html>`;
  }, [uiCode.css, uiCode.html, uiCode.js]);

  return (
    <div className="w-full h-full flex flex-col bg-arsist-bg">
      {/* ヘッダー */}
      <div className="h-10 bg-arsist-surface border-b border-arsist-border flex items-center justify-between px-3">
        <div className="flex items-center gap-1">
          {/* ファイルタブ */}
          {[
            { name: 'ui.html', type: 'html' as const },
            { name: 'style.css', type: 'css' as const },
            { name: 'logic.js', type: 'js' as const },
          ].map(file => (
            <button
              key={file.type}
              onClick={() => setActiveFile(file.type)}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded text-xs transition-colors ${
                activeFile === file.type
                  ? 'bg-arsist-active text-arsist-text'
                  : 'text-arsist-muted hover:bg-arsist-hover hover:text-arsist-text'
              }`}
            >
              {getFileIcon(file.type)}
              <span>{file.name}</span>
            </button>
          ))}
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={() => {
              const result = syncUIFromCode();
              if (!result.success) {
                addNotification({ type: 'error', message: `GUI同期に失敗しました: ${result.error}` });
              } else {
                addNotification({ type: 'success', message: 'GUIに反映しました' });
              }
            }}
            className="btn-icon"
            title="コードをGUIに反映"
          >
            <ChevronRight size={16} />
          </button>
          <button
            onClick={() => {
              syncCodeFromUI();
              addNotification({ type: 'success', message: 'GUIからHTMLを更新しました' });
            }}
            className="btn-icon"
            title="GUIをコードに反映"
          >
            <RefreshCw size={16} />
          </button>
          <button
            onClick={handleCopy}
            className="btn-icon"
            title="コピー"
          >
            {copied ? <Check size={16} className="text-arsist-success" /> : <Copy size={16} />}
          </button>
          <button
            onClick={() => setShowPreview(!showPreview)}
            className={`btn-icon ${showPreview ? 'text-arsist-accent' : ''}`}
            title="プレビュー表示"
          >
            <Eye size={16} />
          </button>
        </div>
      </div>

      {/* メインエリア */}
      <div className="flex-1 flex overflow-hidden">
        {/* コードエディタ */}
        <div className={`${showPreview ? 'w-1/2' : 'w-full'} flex flex-col border-r border-arsist-border`}>
          {/* エディタツールバー */}
          <div className="h-8 bg-arsist-hover border-b border-arsist-border flex items-center justify-between px-3 text-xs text-arsist-muted">
            <span>
              {activeFile === 'html' && 'HTML - UI構造を定義'}
              {activeFile === 'css' && 'CSS - スタイルを定義'}
              {activeFile === 'js' && 'JavaScript - ロジックを定義'}
            </span>
            {project?.uiAuthoring?.mode === 'code' && (
              <span className="text-arsist-warning">コード専用プロジェクト</span>
            )}
            {project?.uiAuthoring?.mode === 'visual' && (
              <span className="text-arsist-warning">ビジュアル専用プロジェクト</span>
            )}
          </div>

          {/* テキストエリア */}
          <div className="flex-1 overflow-hidden">
            <textarea
              value={activeContent}
              onChange={(e) => handleContentChange(e.target.value)}
              className="w-full h-full p-4 bg-arsist-bg text-arsist-text font-mono text-sm resize-none outline-none"
              spellCheck={false}
              style={{ 
                lineHeight: '1.6',
                tabSize: 2,
              }}
            />
          </div>

          {/* ステータスバー */}
          <div className="h-6 bg-arsist-hover border-t border-arsist-border flex items-center px-3 text-[10px] text-arsist-muted">
            <span>行数: {activeContent.split('\n').length}</span>
            <span className="mx-2">|</span>
            <span>文字数: {activeContent.length}</span>
          </div>
        </div>

        {/* プレビュー */}
        {showPreview && (
          <div className="w-1/2 flex flex-col">
            <div className="h-8 bg-arsist-hover border-b border-arsist-border flex items-center px-3 text-xs text-arsist-muted">
              <Eye size={12} className="mr-2" />
              <span>プレビュー (1920x1080)</span>
            </div>
            <div className="flex-1 overflow-hidden bg-arsist-bg p-4">
              <div 
                className="w-full h-full border border-arsist-border rounded overflow-hidden relative"
                style={{ aspectRatio: '16/9', maxHeight: '100%' }}
              >
                <iframe
                  srcDoc={previewHtml}
                  className="w-full h-full border-0"
                  title="Preview"
                  sandbox="allow-scripts"
                />

                {previewError && (
                  <div className="absolute inset-0 p-4 pointer-events-none">
                    <div className="pointer-events-none bg-black/80 border border-arsist-border rounded p-3 text-xs text-red-300 whitespace-pre-wrap max-h-full overflow-auto">
                      <div className="font-medium mb-2">プレビューできません</div>
                      {previewError}
                    </div>
                  </div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
