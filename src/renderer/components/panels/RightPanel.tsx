import { useProjectStore } from '../../stores/projectStore';
import { useUIStore } from '../../stores/uiStore';
import type { SceneObject, UIElement, Vector3, UIStyle } from '../../../shared/types';
import { Box, Compass } from 'lucide-react';

export function RightPanel() {
  const { currentView } = useUIStore();

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {currentView === 'scene' && <ObjectInspector />}
      {currentView === 'ui' && <UIInspector />}
      {currentView === 'logic' && <NodeInspector />}
      {currentView === 'code' && <CodeInspector />}
    </div>
  );
}

function ObjectInspector() {
  const { project, currentSceneId, selectedObjectIds, updateObject } = useProjectStore();
  const currentScene = project?.scenes.find(s => s.id === currentSceneId);
  const selectedObject = currentScene?.objects.find(o => o.id === selectedObjectIds[0]);

  if (!selectedObject) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-arsist-muted p-4">
        <Box size={32} className="mb-3 opacity-30" />
        <p className="text-sm mb-1">オブジェクト未選択</p>
        <p className="text-xs text-center">シーン内のオブジェクトを<br/>クリックして選択してください</p>
      </div>
    );
  }

  const handleTransformChange = (key: keyof SceneObject['transform'], axis: keyof Vector3, value: number) => {
    const newTransform = { ...selectedObject.transform };
    newTransform[key] = { ...newTransform[key], [axis]: value };
    updateObject(selectedObject.id, { transform: newTransform });
  };

  const handleMaterialChange = (key: string, value: any) => {
    const newMaterial = { 
      ...selectedObject.material!, 
      color: selectedObject.material?.color || '#ffffff',
      [key]: value 
    };
    updateObject(selectedObject.id, { material: newMaterial });
  };

  const handleSelectModel = async () => {
    if (!window.electronAPI) return;
    const path = await window.electronAPI.fs.selectFile([
      { name: 'GLB/GLTF', extensions: ['glb', 'gltf'] },
    ]);
    if (path) {
      updateObject(selectedObject.id, { modelPath: path });
    }
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="panel-header">
        <span className="text-arsist-text">インスペクター</span>
      </div>

      <div className="flex-1 overflow-y-auto p-3 space-y-4">
        <ARContextPanel />
        {/* Name */}
        <div>
          <label className="input-label">名前</label>
          <input
            type="text"
            value={selectedObject.name}
            onChange={(e) => updateObject(selectedObject.id, { name: e.target.value })}
            className="input"
          />
        </div>

        {/* Model */}
        {selectedObject.type === 'model' && (
          <div>
            <label className="input-label">モデル</label>
            <div className="flex gap-2">
              <input
                type="text"
                value={selectedObject.modelPath || ''}
                readOnly
                className="input flex-1"
                placeholder="GLB/GLTFを選択"
              />
              <button onClick={handleSelectModel} className="btn btn-secondary">
                参照
              </button>
            </div>
          </div>
        )}

        {/* Transform */}
        <div>
          <h4 className="font-medium text-sm mb-3">Transform</h4>
          
          <div className="space-y-3">
            <Vector3Input
              label="Position"
              value={selectedObject.transform.position}
              onChange={(axis, value) => handleTransformChange('position', axis, value)}
            />
            <Vector3Input
              label="Rotation"
              value={selectedObject.transform.rotation}
              onChange={(axis, value) => handleTransformChange('rotation', axis, value)}
            />
            <Vector3Input
              label="Scale"
              value={selectedObject.transform.scale}
              onChange={(axis, value) => handleTransformChange('scale', axis, value)}
            />
          </div>
        </div>

        {/* Material */}
        {selectedObject.material && selectedObject.type !== 'model' && (
          <div>
            <h4 className="font-medium text-sm mb-3">Material</h4>
            
            <div className="space-y-3">
              <div>
                <label className="input-label">Color</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={selectedObject.material.color}
                    onChange={(e) => handleMaterialChange('color', e.target.value)}
                    className="w-10 h-10 rounded cursor-pointer"
                  />
                  <input
                    type="text"
                    value={selectedObject.material.color}
                    onChange={(e) => handleMaterialChange('color', e.target.value)}
                    className="input flex-1"
                  />
                </div>
              </div>

              <div>
                <label className="input-label">Metallic</label>
                <input
                  type="range"
                  min="0"
                  max="1"
                  step="0.01"
                  value={selectedObject.material.metallic || 0}
                  onChange={(e) => handleMaterialChange('metallic', parseFloat(e.target.value))}
                  className="w-full"
                />
              </div>

              <div>
                <label className="input-label">Roughness</label>
                <input
                  type="range"
                  min="0"
                  max="1"
                  step="0.01"
                  value={selectedObject.material.roughness || 0.5}
                  onChange={(e) => handleMaterialChange('roughness', parseFloat(e.target.value))}
                  className="w-full"
                />
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

interface Vector3InputProps {
  label: string;
  value: Vector3;
  onChange: (axis: keyof Vector3, value: number) => void;
}

function Vector3Input({ label, value, onChange }: Vector3InputProps) {
  return (
    <div>
      <label className="input-label">{label}</label>
      <div className="flex gap-2">
        {(['x', 'y', 'z'] as const).map(axis => (
          <div key={axis} className="flex-1">
            <div className="flex items-center">
              <span className={`w-5 text-xs font-medium ${
                axis === 'x' ? 'text-red-400' : axis === 'y' ? 'text-green-400' : 'text-blue-400'
              }`}>
                {axis.toUpperCase()}
              </span>
              <input
                type="number"
                step="0.1"
                value={value[axis]}
                onChange={(e) => onChange(axis, parseFloat(e.target.value) || 0)}
                className="input flex-1 text-sm py-1"
              />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function UIInspector() {
  const { project, currentUILayoutId, selectedUIElementId, updateUIElement } = useProjectStore();
  const currentLayout = project?.uiLayouts.find(l => l.id === currentUILayoutId);

  const findElement = (el: UIElement, id: string): UIElement | null => {
    if (el.id === id) return el;
    for (const child of el.children || []) {
      const found = findElement(child, id);
      if (found) return found;
    }
    return null;
  };

  const selectedElement = currentLayout && selectedUIElementId 
    ? findElement(currentLayout.root, selectedUIElementId)
    : null;

  if (!selectedElement) {
    return (
      <div className="h-full flex items-center justify-center text-arsist-muted text-sm">
        UI要素を選択してください
      </div>
    );
  }

  const handleStyleChange = (key: keyof UIStyle, value: any) => {
    updateUIElement(selectedElement.id, {
      style: { ...selectedElement.style, [key]: value }
    });
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="panel-header">
        <span>UIインスペクター</span>
      </div>

      <div className="flex-1 overflow-y-auto p-4 space-y-6">
        <ARContextPanel />
        {/* Type */}
        <div>
          <label className="input-label">タイプ</label>
          <select
            value={selectedElement.type}
            onChange={(e) => updateUIElement(selectedElement.id, { type: e.target.value as any })}
            className="input"
          >
            <option value="Panel">Panel</option>
            <option value="Text">Text</option>
            <option value="Button">Button</option>
            <option value="Image">Image</option>
            <option value="Input">Input</option>
            <option value="Slider">Slider</option>
          </select>
        </div>

        {/* Content (for Text, Button) */}
        {(selectedElement.type === 'Text' || selectedElement.type === 'Button') && (
          <div>
            <label className="input-label">コンテンツ</label>
            <input
              type="text"
              value={selectedElement.content || ''}
              onChange={(e) => updateUIElement(selectedElement.id, { content: e.target.value })}
              className="input"
            />
          </div>
        )}

        {/* Layout */}
        <div>
          <label className="input-label">レイアウト</label>
          <select
            value={selectedElement.layout || 'FlexColumn'}
            onChange={(e) => updateUIElement(selectedElement.id, { layout: e.target.value as any })}
            className="input"
          >
            <option value="FlexRow">Flex Row</option>
            <option value="FlexColumn">Flex Column</option>
            <option value="Grid">Grid</option>
            <option value="Absolute">Absolute</option>
          </select>
        </div>

        {/* Style Properties */}
        <div>
          <h4 className="font-medium text-sm mb-3">スタイル</h4>
          
          <div className="space-y-3">
            <div>
              <label className="input-label">背景色</label>
              <div className="flex items-center gap-2">
                <input
                  type="color"
                  value={selectedElement.style.backgroundColor?.substring(0, 7) || '#000000'}
                  onChange={(e) => handleStyleChange('backgroundColor', e.target.value)}
                  className="w-10 h-10 rounded cursor-pointer"
                />
                <input
                  type="text"
                  value={selectedElement.style.backgroundColor || ''}
                  onChange={(e) => handleStyleChange('backgroundColor', e.target.value)}
                  className="input flex-1"
                  placeholder="#00000088"
                />
              </div>
            </div>

            <div>
              <label className="input-label">角丸</label>
              <input
                type="number"
                value={selectedElement.style.borderRadius || 0}
                onChange={(e) => handleStyleChange('borderRadius', parseInt(e.target.value) || 0)}
                className="input"
              />
            </div>

            <div>
              <label className="input-label">ブラー</label>
              <input
                type="number"
                value={selectedElement.style.blur || 0}
                onChange={(e) => handleStyleChange('blur', parseInt(e.target.value) || 0)}
                className="input"
              />
            </div>

            {selectedElement.type === 'Text' && (
              <>
                <div>
                  <label className="input-label">フォントサイズ</label>
                  <input
                    type="number"
                    value={selectedElement.style.fontSize || 16}
                    onChange={(e) => handleStyleChange('fontSize', parseInt(e.target.value) || 16)}
                    className="input"
                  />
                </div>
                <div>
                  <label className="input-label">テキスト色</label>
                  <div className="flex items-center gap-2">
                    <input
                      type="color"
                      value={selectedElement.style.color || '#FFFFFF'}
                      onChange={(e) => handleStyleChange('color', e.target.value)}
                      className="w-10 h-10 rounded cursor-pointer"
                    />
                    <input
                      type="text"
                      value={selectedElement.style.color || '#FFFFFF'}
                      onChange={(e) => handleStyleChange('color', e.target.value)}
                      className="input flex-1"
                    />
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function NodeInspector() {
  const { project, currentLogicGraphId, selectedNodeIds, updateLogicNode } = useProjectStore();
  const currentGraph = project?.logicGraphs.find(g => g.id === currentLogicGraphId);
  const selectedNode = currentGraph?.nodes.find(n => n.id === selectedNodeIds[0]);

  if (!selectedNode) {
    return (
      <div className="h-full flex items-center justify-center text-arsist-muted text-sm">
        ノードを選択してください
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="panel-header">
        <span>ノードインスペクター</span>
      </div>

      <div className="flex-1 overflow-y-auto p-4 space-y-6">
        <div>
          <label className="input-label">タイプ</label>
          <div className="px-3 py-2 bg-arsist-bg rounded text-sm">
            {selectedNode.type}
          </div>
        </div>

        {selectedNode.eventType && (
          <div>
            <label className="input-label">イベント</label>
            <select
              value={selectedNode.eventType}
              onChange={(e) => updateLogicNode(selectedNode.id, { eventType: e.target.value })}
              className="input"
            >
              <option value="OnStart">OnStart</option>
              <option value="OnUpdate">OnUpdate</option>
              <option value="OnGazeEnter">OnGazeEnter</option>
              <option value="OnGazeExit">OnGazeExit</option>
              <option value="OnTap">OnTap</option>
            </select>
          </div>
        )}

        <div>
          <label className="input-label">位置</label>
          <div className="flex gap-2">
            <div className="flex-1">
              <span className="text-xs text-arsist-muted">X</span>
              <input
                type="number"
                value={selectedNode.position.x}
                onChange={(e) => updateLogicNode(selectedNode.id, {
                  position: { ...selectedNode.position, x: parseInt(e.target.value) || 0 }
                })}
                className="input"
              />
            </div>
            <div className="flex-1">
              <span className="text-xs text-arsist-muted">Y</span>
              <input
                type="number"
                value={selectedNode.position.y}
                onChange={(e) => updateLogicNode(selectedNode.id, {
                  position: { ...selectedNode.position, y: parseInt(e.target.value) || 0 }
                })}
                className="input"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function CodeInspector() {
  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="panel-header">
        <span className="text-arsist-text">コードヘルプ</span>
      </div>

      <div className="flex-1 overflow-y-auto p-3 space-y-4">
        <div>
          <h4 className="text-xs font-medium text-arsist-accent mb-2">Arsist Bridge API</h4>
          <div className="space-y-2 text-xs">
            <div className="p-2 bg-arsist-bg rounded">
              <code className="text-arsist-warning">ArsistBridge.sendEvent(name, data)</code>
              <p className="text-arsist-muted mt-1">エンジンにイベントを送信</p>
            </div>
            <div className="p-2 bg-arsist-bg rounded">
              <code className="text-arsist-warning">ArsistBridge.getData(key)</code>
              <p className="text-arsist-muted mt-1">エンジンからデータを取得</p>
            </div>
            <div className="p-2 bg-arsist-bg rounded">
              <code className="text-arsist-warning">window.onArsistData(data)</code>
              <p className="text-arsist-muted mt-1">データ受信コールバック</p>
            </div>
          </div>
        </div>

        <div>
          <h4 className="text-xs font-medium text-arsist-primary mb-2">推奨CSS</h4>
          <div className="space-y-1 text-[10px] text-arsist-muted">
            <p>• backdrop-filter: blur() でガラス効果</p>
            <p>• rgba() で半透明背景</p>
            <p>• ARグラス用に高コントラスト推奨</p>
          </div>
        </div>

        <div>
          <h4 className="text-xs font-medium text-orange-400 mb-2">解像度</h4>
          <p className="text-xs text-arsist-muted">
            XREAL One: 1920×1080
          </p>
        </div>
      </div>
    </div>
  );
}

function ARContextPanel() {
  const { project, updateARSettings } = useProjectStore();
  const tracking = project?.arSettings?.trackingMode || '6dof';
  const presentation = project?.arSettings?.presentationMode || 'world_anchored';
  const floating = project?.arSettings?.floatingScreen;

  const trackingLabel = tracking === '6dof'
    ? '6DoF (空間移動 + 回転)'
    : tracking === '3dof'
      ? '3DoF (回転のみ)'
      : 'Head-Locked (固定表示)';

  const presentationLabel = presentation === 'floating_screen'
    ? 'Floating Screen (視線前方に固定)' 
    : presentation === 'head_locked_hud'
      ? 'HUD (視界固定)'
      : 'World Anchored (空間固定)';

  return (
    <div className="p-3 bg-arsist-bg border border-arsist-border rounded text-xs space-y-3">
      <div className="flex items-center gap-2 text-arsist-accent">
        <Compass size={14} />
        <span className="font-medium">AR動作モード</span>
      </div>
      <div className="text-arsist-muted space-y-1">
        <div>Tracking: <span className="text-arsist-text">{trackingLabel}</span></div>
        <div>Presentation: <span className="text-arsist-text">{presentationLabel}</span></div>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <div>
          <label className="input-label">トラッキング</label>
          <select
            value={tracking}
            onChange={(e) => updateARSettings({ trackingMode: e.target.value as any })}
            className="input"
          >
            <option value="6dof">6DoF</option>
            <option value="3dof">3DoF</option>
            <option value="head_locked">Head-Locked</option>
          </select>
        </div>
        <div>
          <label className="input-label">表示モード</label>
          <select
            value={presentation}
            onChange={(e) => updateARSettings({ presentationMode: e.target.value as any })}
            className="input"
          >
            <option value="world_anchored">World Anchored</option>
            <option value="floating_screen">Floating Screen</option>
            <option value="head_locked_hud">Head-Locked HUD</option>
          </select>
        </div>
      </div>

      {presentation === 'floating_screen' && (
        <div className="grid grid-cols-3 gap-2">
          <div>
            <label className="input-label">幅 (m)</label>
            <input
              type="number"
              step="0.1"
              value={floating?.width ?? 1.6}
              onChange={(e) => updateARSettings({ floatingScreen: { width: Number(e.target.value) } as any })}
              className="input"
            />
          </div>
          <div>
            <label className="input-label">高さ (m)</label>
            <input
              type="number"
              step="0.1"
              value={floating?.height ?? 0.9}
              onChange={(e) => updateARSettings({ floatingScreen: { height: Number(e.target.value) } as any })}
              className="input"
            />
          </div>
          <div>
            <label className="input-label">距離 (m)</label>
            <input
              type="number"
              step="0.1"
              value={floating?.distance ?? 2}
              onChange={(e) => updateARSettings({ floatingScreen: { distance: Number(e.target.value) } as any })}
              className="input"
            />
          </div>
        </div>
      )}
    </div>
  );
}
