/**
 * ScriptEditor — スクリプト編集インスペクター
 * スクリプトビューのプロパティ設定
 */

export function ScriptInspector() {
  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="panel-header"><span>Script Inspector</span></div>
      <div className="flex-1 overflow-y-auto p-3 space-y-4">
        <div className="text-arsist-muted text-sm">
          スクリプト設定はまだ実装されていません
        </div>
      </div>
    </div>
  );
}
