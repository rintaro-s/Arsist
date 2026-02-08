/**
 * DataFlowEditor — DataSource / Transform 設定画面
 *
 * 中央エリアに DataSource → Transform → DataStore の
 * パイプラインを視覚的に表示し、追加・設定を行う。
 */
import React from 'react';
import { useProjectStore } from '../../stores/projectStore';
import type {
  DataSourceDefinition,
  DataSourceType,
  TransformDefinition,
  TransformType,
} from '../../../shared/types';
import {
  Radio,
  Wifi,
  MapPin,
  Clock,
  Mic,
  MessageSquare,
  Activity,
  Hand,
  Smartphone,
  Plus,
  Trash2,
  ArrowRight,
} from 'lucide-react';

/* ── DataSource カテゴリ ── */
const DS_CATEGORIES: {
  label: string;
  items: { type: DataSourceType; label: string; icon: React.ReactNode; desc: string }[];
}[] = [
  {
    label: 'センサー/デバイス',
    items: [
      { type: 'XR_Tracker', label: 'XR Tracker', icon: <Activity size={14} />, desc: '頭部姿勢・回転' },
      { type: 'XR_HandPose', label: 'Hand Pose', icon: <Hand size={14} />, desc: '手の関節・ジェスチャー' },
      { type: 'Device_Status', label: 'Device Status', icon: <Smartphone size={14} />, desc: 'バッテリー・温度・装着' },
      { type: 'Location_Provider', label: 'Location', icon: <MapPin size={14} />, desc: 'GPS・高度・速度' },
    ],
  },
  {
    label: 'ネットワーク',
    items: [
      { type: 'REST_Client', label: 'REST API', icon: <Radio size={14} />, desc: 'HTTP GET/POST' },
      { type: 'WebSocket_Stream', label: 'WebSocket', icon: <Wifi size={14} />, desc: 'リアルタイムストリーム' },
      { type: 'MQTT_Subscriber', label: 'MQTT', icon: <MessageSquare size={14} />, desc: 'IoTメッセージ受信' },
    ],
  },
  {
    label: 'システム',
    items: [
      { type: 'System_Clock', label: 'System Clock', icon: <Clock size={14} />, desc: '現在時刻' },
      { type: 'Voice_Recognition', label: 'Voice Recog', icon: <Mic size={14} />, desc: '音声→テキスト' },
      { type: 'Microphone_Level', label: 'Mic Level', icon: <Activity size={14} />, desc: '音量レベル' },
    ],
  },
];

const TRANSFORM_TYPES: { type: TransformType; label: string; desc: string }[] = [
  { type: 'Formula', label: 'Formula', desc: '自由数式 (例: val * 1.8 + 32)' },
  { type: 'Clamper', label: 'Clamper', desc: '値を範囲に制限' },
  { type: 'Remap', label: 'Remap', desc: '入力範囲→出力範囲の線形変換' },
  { type: 'Smoother', label: 'Smoother', desc: '移動平均で平滑化' },
  { type: 'Comparator', label: 'Comparator', desc: 'A > B → true/false' },
  { type: 'Threshold', label: 'Threshold', desc: 'しきい値でフラグ出力' },
  { type: 'State_Mapper', label: 'State Mapper', desc: '値→ラベル変換' },
  { type: 'String_Template', label: 'String Template', desc: '文字列テンプレート' },
  { type: 'Time_Formatter', label: 'Time Formatter', desc: '秒 → mm:ss 等' },
  { type: 'History_Buffer', label: 'History Buffer', desc: '過去N件をグラフ用配列に' },
  { type: 'Accumulator', label: 'Accumulator', desc: '値を累積' },
];

export function DataFlowEditor() {
  const {
    project,
    addDataSource,
    removeDataSource,
    selectedDataSourceId,
    selectDataSource,
    addTransform,
    removeTransform,
    selectedTransformId,
    selectTransform,
  } = useProjectStore();

  if (!project) {
    return (
      <div className="w-full h-full flex items-center justify-center text-arsist-muted text-sm">
        プロジェクトを開いてください
      </div>
    );
  }

  const sources = project.dataFlow.dataSources;
  const transforms = project.dataFlow.transforms;

  return (
    <div className="w-full h-full flex flex-col overflow-hidden">
      {/* ── ヘッダー ── */}
      <div className="h-8 bg-arsist-surface border-b border-arsist-border flex items-center px-3 text-[11px] text-arsist-muted gap-4 shrink-0">
        <span className="font-medium text-arsist-text">DataFlow</span>
        <span>Sources: {sources.length}</span>
        <span>Transforms: {transforms.length}</span>
      </div>

      {/* ── 本体 ── */}
      <div className="flex-1 overflow-auto p-4">
        {/* パイプライン図 */}
        <div className="flex items-start gap-6">
          {/* DataSources 列 */}
          <div className="flex-1 min-w-[280px]">
            <SectionHeader title="DataSource" count={sources.length} onAdd={() => {/* ドロップダウンは下のカテゴリから */ }} />

            {/* 追加ドロップダウン */}
            <div className="mb-3 space-y-2">
              {DS_CATEGORIES.map((cat) => (
                <div key={cat.label}>
                  <div className="text-[10px] text-arsist-muted uppercase tracking-wider mb-1">{cat.label}</div>
                  <div className="flex flex-wrap gap-1">
                    {cat.items.map((item) => (
                      <button
                        key={item.type}
                        onClick={() =>
                          addDataSource({
                            type: item.type,
                            mode: 'polling',
                            storeAs: item.type.toLowerCase(),
                          })
                        }
                        className="flex items-center gap-1 px-2 py-1 rounded border border-arsist-border bg-arsist-surface hover:bg-arsist-hover text-[11px] text-arsist-muted hover:text-arsist-text transition-colors"
                        title={item.desc}
                      >
                        {item.icon}
                        <span>{item.label}</span>
                        <Plus size={10} className="ml-1 opacity-50" />
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>

            {/* 既存ソース一覧 */}
            <div className="space-y-1">
              {sources.map((ds) => (
                <SourceCard
                  key={ds.id}
                  source={ds}
                  isSelected={ds.id === selectedDataSourceId}
                  onSelect={() => selectDataSource(ds.id)}
                  onRemove={() => removeDataSource(ds.id)}
                />
              ))}
            </div>
          </div>

          {/* 矢印 */}
          <div className="flex items-center pt-20 text-arsist-muted">
            <ArrowRight size={20} />
          </div>

          {/* Transforms 列 */}
          <div className="flex-1 min-w-[280px]">
            <SectionHeader title="Transform" count={transforms.length} onAdd={() => {}} />

            {/* 追加ボタン */}
            <div className="mb-3 flex flex-wrap gap-1">
              {TRANSFORM_TYPES.map((t) => (
                <button
                  key={t.type}
                  onClick={() =>
                    addTransform({
                      type: t.type,
                      inputs: [],
                      storeAs: t.type.toLowerCase(),
                    })
                  }
                  className="px-2 py-1 rounded border border-arsist-border bg-arsist-surface hover:bg-arsist-hover text-[11px] text-arsist-muted hover:text-arsist-text transition-colors"
                  title={t.desc}
                >
                  {t.label}
                  <Plus size={10} className="ml-1 inline opacity-50" />
                </button>
              ))}
            </div>

            {/* 既存 Transform 一覧 */}
            <div className="space-y-1">
              {transforms.map((tf) => (
                <TransformCard
                  key={tf.id}
                  transform={tf}
                  isSelected={tf.id === selectedTransformId}
                  onSelect={() => selectTransform(tf.id)}
                  onRemove={() => removeTransform(tf.id)}
                />
              ))}
            </div>
          </div>

          {/* 矢印 */}
          <div className="flex items-center pt-20 text-arsist-muted">
            <ArrowRight size={20} />
          </div>

          {/* DataStore 列 (読み取り専用) */}
          <div className="flex-1 min-w-[200px]">
            <SectionHeader title="DataStore キー" count={sources.length + transforms.length} />
            <div className="space-y-1">
              {sources.map((ds) => (
                <div key={ds.id} className="px-2 py-1.5 rounded bg-arsist-bg border border-arsist-border text-[11px] font-mono text-arsist-accent">
                  {ds.storeAs}
                </div>
              ))}
              {transforms.map((tf) => (
                <div key={tf.id} className="px-2 py-1.5 rounded bg-arsist-bg border border-arsist-border text-[11px] font-mono text-arsist-warning">
                  {tf.storeAs}
                </div>
              ))}
              {sources.length === 0 && transforms.length === 0 && (
                <div className="text-[11px] text-arsist-muted py-4 text-center">
                  DataSource を追加すると<br />ここにキーが表示されます
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ── Sub components ── */

function SectionHeader({ title, count, onAdd }: { title: string; count: number; onAdd?: () => void }) {
  return (
    <div className="flex items-center gap-2 mb-2">
      <span className="text-xs font-medium text-arsist-text">{title}</span>
      <span className="text-[10px] text-arsist-muted">({count})</span>
    </div>
  );
}

function SourceCard({ source, isSelected, onSelect, onRemove }: {
  source: DataSourceDefinition; isSelected: boolean; onSelect: () => void; onRemove: () => void;
}) {
  const meta = DS_CATEGORIES.flatMap((c) => c.items).find((i) => i.type === source.type);
  return (
    <div
      onClick={onSelect}
      className={`flex items-center gap-2 px-2 py-1.5 rounded border cursor-pointer transition-colors ${
        isSelected
          ? 'border-arsist-accent bg-arsist-accent/10'
          : 'border-arsist-border bg-arsist-surface hover:bg-arsist-hover'
      }`}
    >
      <div className="text-arsist-muted">{meta?.icon ?? <Radio size={14} />}</div>
      <div className="flex-1 min-w-0">
        <div className="text-[11px] text-arsist-text truncate">{meta?.label ?? source.type}</div>
        <div className="text-[10px] font-mono text-arsist-accent truncate">{source.storeAs}</div>
      </div>
      <button onClick={(e) => { e.stopPropagation(); onRemove(); }} className="text-arsist-muted hover:text-arsist-error p-0.5">
        <Trash2 size={12} />
      </button>
    </div>
  );
}

function TransformCard({ transform, isSelected, onSelect, onRemove }: {
  transform: TransformDefinition; isSelected: boolean; onSelect: () => void; onRemove: () => void;
}) {
  return (
    <div
      onClick={onSelect}
      className={`flex items-center gap-2 px-2 py-1.5 rounded border cursor-pointer transition-colors ${
        isSelected
          ? 'border-arsist-warning bg-arsist-warning/10'
          : 'border-arsist-border bg-arsist-surface hover:bg-arsist-hover'
      }`}
    >
      <div className="flex-1 min-w-0">
        <div className="text-[11px] text-arsist-text truncate">{transform.type}</div>
        <div className="text-[10px] font-mono text-arsist-warning truncate">{transform.storeAs}</div>
        {transform.expression && (
          <div className="text-[10px] font-mono text-arsist-muted truncate">{transform.expression}</div>
        )}
      </div>
      <button onClick={(e) => { e.stopPropagation(); onRemove(); }} className="text-arsist-muted hover:text-arsist-error p-0.5">
        <Trash2 size={12} />
      </button>
    </div>
  );
}
