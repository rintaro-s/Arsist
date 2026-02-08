/**
 * BottomPanel — コンソール + DataStoreモニター
 */
import { useUIStore } from '../../stores/uiStore';
import { useProjectStore } from '../../stores/projectStore';
import { useDataStore } from '../../stores/dataStoreContext';
import { Trash2 } from 'lucide-react';

export function BottomPanel() {
  const { bottomTab, setBottomTab, consoleLogs, clearConsoleLogs, buildLogs } = useUIStore();

  return (
    <div className="h-full flex flex-col overflow-hidden bg-arsist-surface">
      {/* タブ */}
      <div className="h-7 flex items-center border-b border-arsist-border px-2 gap-1 shrink-0">
        <TabBtn label="Console" active={bottomTab === 'console'} onClick={() => setBottomTab('console')} />
        <TabBtn label="DataStore" active={bottomTab === 'datastore'} onClick={() => setBottomTab('datastore')} />

        {bottomTab === 'console' && (
          <button onClick={clearConsoleLogs} className="ml-auto btn-icon p-0.5" title="クリア">
            <Trash2 size={12} />
          </button>
        )}
      </div>

      {/* コンテンツ */}
      <div className="flex-1 overflow-y-auto font-mono text-[11px]">
        {bottomTab === 'console' && <ConsoleView logs={consoleLogs} buildLogs={buildLogs} />}
        {bottomTab === 'datastore' && <DataStoreView />}
      </div>
    </div>
  );
}

function TabBtn({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`px-2 py-0.5 rounded text-[11px] transition-colors ${
        active ? 'bg-arsist-active text-arsist-accent' : 'text-arsist-muted hover:bg-arsist-hover'
      }`}
    >
      {label}
    </button>
  );
}

function ConsoleView({ logs, buildLogs }: { logs: { type: string; message: string; time: string }[]; buildLogs: string[] }) {
  const allLogs = [
    ...logs.map((l) => ({ ...l, source: 'app' })),
    ...buildLogs.map((msg) => ({ type: 'info', message: msg, time: '', source: 'build' })),
  ];

  return (
    <div className="p-2 space-y-0.5">
      {allLogs.map((log, i) => (
        <div key={i} className={`flex items-start gap-2 py-0.5 ${
          log.type === 'error' ? 'text-arsist-error' :
          log.type === 'warning' ? 'text-arsist-warning' :
          'text-arsist-muted'
        }`}>
          {log.time && <span className="text-arsist-muted shrink-0">{log.time}</span>}
          <span className="break-all">{log.message}</span>
        </div>
      ))}
      {allLogs.length === 0 && (
        <div className="text-arsist-muted text-center py-3">ログなし</div>
      )}
    </div>
  );
}

function DataStoreView() {
  const { project } = useProjectStore();

  let storeCtx: { data: Record<string, unknown> } | null = null;
  try {
    // eslint-disable-next-line react-hooks/rules-of-hooks
    storeCtx = useDataStore();
  } catch {
    // DataStoreProvider 外の場合
  }

  const sources = project?.dataFlow.dataSources || [];
  const transforms = project?.dataFlow.transforms || [];
  const keys = [...sources.map((s) => s.storeAs), ...transforms.map((t) => t.storeAs)];
  const data = storeCtx?.data || {};

  return (
    <div className="p-2">
      <table className="w-full">
        <thead>
          <tr className="text-[10px] text-arsist-muted uppercase">
            <th className="text-left py-1 pr-2">Key</th>
            <th className="text-left py-1">Value</th>
          </tr>
        </thead>
        <tbody>
          {keys.map((key) => (
            <tr key={key} className="border-t border-arsist-border/30">
              <td className="py-0.5 pr-2 text-arsist-accent">{key}</td>
              <td className="py-0.5 text-arsist-text">{data[key] != null ? JSON.stringify(data[key]) : <span className="text-arsist-muted">—</span>}</td>
            </tr>
          ))}
          {keys.length === 0 && (
            <tr>
              <td colSpan={2} className="text-arsist-muted text-center py-3">DataSource を追加するとキーが表示されます</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
