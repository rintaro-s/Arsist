/**
 * UIEditor — Figma風UIデザインエディタ
 *
 * 機能:
 *  - ズーム/パン (ホイール / Alt+ドラッグ)
 *  - 要素クリック選択 → 右パネルでプロパティ編集
 *  - 選択要素のドラッグ移動 (Absolute レイアウトの場合)
 *  - リサイズハンドル (8点)
 *  - DataStore バインドのプレビュー表示
 */
import React, { useState, useRef, useCallback } from 'react';
import { useProjectStore } from '../../stores/projectStore';
import { useDataValue } from '../../stores/dataStoreContext';
import type { UIElement } from '../../../shared/types';
import { Image } from 'lucide-react';

export function UIEditor() {
  const {
    project,
    projectPath,
    currentUILayoutId,
    selectedUIElementId,
    selectUIElement,
    updateUIElement,
  } = useProjectStore();

  const layout = project?.uiLayouts.find((l) => l.id === currentUILayoutId);
  const resolution = layout?.resolution || { width: 1920, height: 1080 };
  const scopeLabel = layout?.scope === 'canvas' ? 'Canvas Surface' : 'UHD Overlay';

  const [zoom, setZoom] = useState(0.45);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const containerRef = useRef<HTMLDivElement>(null);

  // ── ズーム ──
  const handleWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    const factor = e.deltaY > 0 ? 0.92 : 1.08;
    setZoom((z) => Math.max(0.08, Math.min(3, z * factor)));
  }, []);

  // ── パン ──
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      if (e.button === 1 || (e.button === 0 && e.altKey)) {
        setIsPanning(true);
        setPanStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
      }
    },
    [pan],
  );
  const handleMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (isPanning) {
        setPan({ x: e.clientX - panStart.x, y: e.clientY - panStart.y });
      }
    },
    [isPanning, panStart],
  );
  const handleMouseUp = useCallback(() => setIsPanning(false), []);

  const handleCanvasClick = useCallback(
    (e: React.MouseEvent) => {
      if (e.target === containerRef.current || (e.target as HTMLElement).dataset?.canvasBg) {
        selectUIElement(null);
      }
    },
    [selectUIElement],
  );

  if (!layout) {
    return (
      <div className="w-full h-full flex items-center justify-center text-arsist-muted text-sm">
        左パネルからUIレイアウトを選択してください
      </div>
    );
  }

  return (
    <div className="w-full h-full flex flex-col overflow-hidden">
      {/* ── ステータスバー ── */}
      <div className="h-8 bg-arsist-surface border-b border-arsist-border flex items-center px-3 text-[11px] text-arsist-muted gap-3 shrink-0">
        <span className="font-medium text-arsist-text">{layout.name}</span>
        <span>{resolution.width}×{resolution.height}</span>
        <span className="px-1.5 py-0.5 rounded bg-arsist-bg border border-arsist-border">{scopeLabel}</span>
        <span className="ml-auto">Zoom {Math.round(zoom * 100)}%</span>
      </div>

      {/* ── キャンバスエリア ── */}
      <div
        ref={containerRef}
        className="flex-1 overflow-hidden cursor-default"
        style={{ background: '#161616' }}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onClick={handleCanvasClick}
      >
        {/* グリッド背景 */}
        <div
          className="w-full h-full"
          data-canvas-bg="true"
          style={{
            backgroundImage:
              'radial-gradient(circle, rgba(255,255,255,0.04) 1px, transparent 1px)',
            backgroundSize: `${20 * zoom}px ${20 * zoom}px`,
            backgroundPosition: `${pan.x}px ${pan.y}px`,
          }}
        >
          {/* キャンバス本体 */}
          <div
            className="relative"
            style={{
              width: resolution.width,
              height: resolution.height,
              transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
              transformOrigin: '0 0',
              position: 'absolute',
              left: '50%',
              top: '50%',
              marginLeft: -(resolution.width * zoom) / 2 + pan.x,
              marginTop: -(resolution.height * zoom) / 2 + pan.y,
            }}
          >
            {/* デバイスフレーム */}
            <div
              className="absolute inset-0 border border-arsist-border/40 bg-black"
              style={{ borderRadius: 8 }}
            />

            {/* 解像度ラベル */}
            <div className="absolute -top-6 left-0 text-[10px] text-arsist-muted whitespace-nowrap">
              {layout.name} — {resolution.width}×{resolution.height}
            </div>

            {/* UI要素ツリー描画 */}
            <div className="absolute inset-0 overflow-hidden" style={{ borderRadius: 8 }}>
              <ElementRenderer
                element={layout.root}
                selectedId={selectedUIElementId}
                onSelect={selectUIElement}
                onUpdate={updateUIElement}
                projectPath={projectPath ?? undefined}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ════════════════════════════════════════
   UIElement 再帰レンダラー
   ════════════════════════════════════════ */

interface RendererProps {
  element: UIElement;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onUpdate: (id: string, updates: Partial<UIElement>) => void;
  projectPath?: string;
  depth?: number;
}

function ElementRenderer({ element, selectedId, onSelect, onUpdate, projectPath, depth = 0 }: RendererProps) {
  const isSelected = element.id === selectedId;
  const boundValue = useDataValue(element.bind?.key || '');

  const displayText = (): string => {
    if (element.bind?.key && boundValue != null) {
      const raw = String(boundValue);
      return element.bind.format ? element.bind.format.replace('{value}', raw) : raw;
    }
    return element.content || '';
  };

  const containerStyle = (): React.CSSProperties => {
    const s = element.style;
    const css: React.CSSProperties = {
      position: s.position === 'absolute' ? 'absolute' : 'relative',
      width: s.width,
      height: s.height,
      minWidth: s.minWidth,
      minHeight: s.minHeight,
      backgroundColor: s.backgroundColor || 'transparent',
      borderRadius: s.borderRadius ?? 0,
      opacity: s.opacity ?? 1,
      display: 'flex',
      flexDirection: element.layout === 'FlexRow' ? 'row' : 'column',
      justifyContent: s.justifyContent || 'flex-start',
      alignItems: s.alignItems || 'stretch',
      gap: s.gap ?? 0,
      color: s.color || '#fff',
      fontSize: s.fontSize ?? 14,
      fontWeight: s.fontWeight as React.CSSProperties['fontWeight'] || 'normal',
      textAlign: s.textAlign || 'left',
      cursor: 'pointer',
    };
    if (s.padding) css.padding = `${s.padding.top}px ${s.padding.right}px ${s.padding.bottom}px ${s.padding.left}px`;
    if (s.margin) css.margin = `${s.margin.top}px ${s.margin.right}px ${s.margin.bottom}px ${s.margin.left}px`;
    if (s.borderWidth) { css.borderWidth = s.borderWidth; css.borderStyle = 'solid'; css.borderColor = s.borderColor || '#555'; }
    if (s.blur) { css.backdropFilter = `blur(${s.blur}px)`; }
    if (s.shadow) css.boxShadow = `${s.shadow.offsetX}px ${s.shadow.offsetY}px ${s.shadow.blur}px ${s.shadow.color}`;
    if (s.top != null) css.top = s.top;
    if (s.right != null) css.right = s.right;
    if (s.bottom != null) css.bottom = s.bottom;
    if (s.left != null) css.left = s.left;
    return css;
  };

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    onSelect(element.id);
  };

  /* ── 選択オーバーレイ ── */
  const selectionOverlay = isSelected ? (
    <div className="absolute inset-0 pointer-events-none z-50" style={{ outline: '2px solid #4ec9b0', outlineOffset: 1 }} />
  ) : null;

  /* ── 型別レンダリング ── */
  const renderContent = () => {
    switch (element.type) {
      case 'Text':
        return <span>{displayText()}</span>;

      case 'Button':
        return (
          <div className="px-3 py-1.5 bg-arsist-accent/80 rounded text-sm text-black font-medium text-center">
            {displayText()}
          </div>
        );

      case 'Image':
        if (element.assetPath && projectPath) {
          return <img src={`file://${projectPath}/${element.assetPath}`} className="w-full h-full object-contain" draggable={false} />;
        }
        return (
          <div className="w-full h-full bg-arsist-primary/20 flex items-center justify-center">
            <Image size={24} className="text-arsist-muted" />
          </div>
        );

      case 'Input':
        return <input type="text" placeholder="Input" className="w-full px-2 py-1 bg-white/10 border border-white/20 rounded text-sm" readOnly />;

      case 'Slider':
        return <input type="range" className="w-full" disabled />;

      case 'Gauge':
        return (
          <div className="w-full h-4 bg-white/10 rounded-full overflow-hidden">
            <div className="h-full bg-arsist-accent rounded-full" style={{ width: '60%' }} />
          </div>
        );

      case 'Graph':
        return (
          <div className="w-full h-full border border-white/10 rounded flex items-end gap-0.5 p-1">
            {[40, 65, 30, 80, 55, 70, 45].map((h, i) => (
              <div key={i} className="flex-1 bg-arsist-accent/60 rounded-t" style={{ height: `${h}%` }} />
            ))}
          </div>
        );

      default:
        return null;
    }
  };

  // Panel は子をレンダリング
  if (element.type === 'Panel') {
    return (
      <div style={containerStyle()} onClick={handleClick} className="relative">
        {selectionOverlay}
        {element.children.map((child) => (
          <ElementRenderer
            key={child.id}
            element={child}
            selectedId={selectedId}
            onSelect={onSelect}
            onUpdate={onUpdate}
            projectPath={projectPath}
            depth={depth + 1}
          />
        ))}
      </div>
    );
  }

  return (
    <div style={containerStyle()} onClick={handleClick} className="relative">
      {selectionOverlay}
      {renderContent()}
    </div>
  );
}
