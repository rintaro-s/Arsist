import React, { useCallback, useMemo, useState, useRef, useEffect } from 'react';
import { useProjectStore } from '../../stores/projectStore';
import type { LogicNode, LogicConnection } from '../../../shared/types';
import { 
  Play, 
  RefreshCw, 
  Eye, 
  MousePointer, 
  ArrowRight,
  Plus,
  Zap,
  GitBranch,
  Hash,
  Type,
  Box
} from 'lucide-react';

interface Position {
  x: number;
  y: number;
}

export function LogicEditor() {
  const { 
    project, 
    currentLogicGraphId, 
    selectedNodeIds, 
    selectNodes,
    addLogicNode,
    updateLogicNode,
    removeLogicNode,
    addLogicConnection,
    removeLogicConnection
  } = useProjectStore();
  
  const currentGraph = project?.logicGraphs.find(g => g.id === currentLogicGraphId);
  
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState<Position>({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState<Position>({ x: 0, y: 0 });
  const [connectingFrom, setConnectingFrom] = useState<{ nodeId: string; port: string } | null>(null);
  const [mousePos, setMousePos] = useState<Position>({ x: 0, y: 0 });
  const [showNodeMenu, setShowNodeMenu] = useState(false);
  const [nodeMenuPos, setNodeMenuPos] = useState<Position>({ x: 0, y: 0 });
  
  const canvasRef = useRef<HTMLDivElement>(null);

  // Handle zoom
  const handleWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    setZoom(z => Math.max(0.25, Math.min(2, z * delta)));
  }, []);

  // Handle pan
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    if (e.button === 1 || (e.button === 0 && e.altKey)) {
      setIsDragging(true);
      setDragStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
    }
  }, [pan]);

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    if (canvasRef.current) {
      const rect = canvasRef.current.getBoundingClientRect();
      setMousePos({
        x: (e.clientX - rect.left - pan.x) / zoom,
        y: (e.clientY - rect.top - pan.y) / zoom,
      });
    }
    
    if (isDragging) {
      setPan({
        x: e.clientX - dragStart.x,
        y: e.clientY - dragStart.y,
      });
    }
  }, [isDragging, dragStart, pan, zoom]);

  const handleMouseUp = useCallback(() => {
    setIsDragging(false);
    setConnectingFrom(null);
  }, []);

  // Right click to show add node menu
  const handleContextMenu = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    if (canvasRef.current) {
      const rect = canvasRef.current.getBoundingClientRect();
      setNodeMenuPos({
        x: (e.clientX - rect.left - pan.x) / zoom,
        y: (e.clientY - rect.top - pan.y) / zoom,
      });
      setShowNodeMenu(true);
    }
  }, [pan, zoom]);

  const handleCanvasClick = useCallback(() => {
    setShowNodeMenu(false);
    if (!connectingFrom) {
      selectNodes([]);
    }
  }, [connectingFrom, selectNodes]);

  // Add node from menu
  const handleAddNode = (nodeType: string, eventType?: string, actionType?: string) => {
    addLogicNode({
      type: nodeType as LogicNode['type'],
      eventType,
      actionType,
      position: nodeMenuPos,
      inputs: nodeType === 'action' ? ['exec', 'target'] : undefined,
      outputs: nodeType === 'event' ? ['exec'] : nodeType === 'action' ? ['exec'] : ['value'],
    });
    setShowNodeMenu(false);
  };

  // Connection handling
  const handlePortMouseDown = (nodeId: string, port: string, isOutput: boolean) => {
    if (isOutput) {
      setConnectingFrom({ nodeId, port });
    }
  };

  const handlePortMouseUp = (nodeId: string, port: string, isInput: boolean) => {
    if (connectingFrom && isInput && connectingFrom.nodeId !== nodeId) {
      addLogicConnection({
        sourceNodeId: connectingFrom.nodeId,
        sourcePort: connectingFrom.port,
        targetNodeId: nodeId,
        targetPort: port,
      });
    }
    setConnectingFrom(null);
  };

  return (
    <div className="w-full h-full flex flex-col overflow-hidden">
      {/* Toolbar */}
      <div className="h-10 bg-arsist-surface border-b border-arsist-primary/30 flex items-center px-4 gap-2">
        <span className="text-xs text-arsist-muted mr-2">ノード追加:</span>
        <button 
          className="flex items-center gap-1 px-2 py-1 rounded hover:bg-arsist-primary/50 text-xs"
          onClick={() => handleAddNode('event', 'OnStart')}
        >
          <Play size={14} /> OnStart
        </button>
        <button 
          className="flex items-center gap-1 px-2 py-1 rounded hover:bg-arsist-primary/50 text-xs"
          onClick={() => handleAddNode('event', 'OnUpdate')}
        >
          <RefreshCw size={14} /> OnUpdate
        </button>
        <button 
          className="flex items-center gap-1 px-2 py-1 rounded hover:bg-arsist-primary/50 text-xs"
          onClick={() => handleAddNode('action', undefined, 'Debug.Log')}
        >
          <Type size={14} /> Debug.Log
        </button>
        
        <div className="ml-auto flex items-center gap-2 text-xs text-arsist-muted">
          <span>Zoom: {Math.round(zoom * 100)}%</span>
        </div>
      </div>

      {/* Canvas */}
      <div 
        ref={canvasRef}
        className="flex-1 overflow-hidden bg-arsist-bg cursor-default"
        style={{
          backgroundImage: `
            radial-gradient(circle, #0f3460 1px, transparent 1px)
          `,
          backgroundSize: `${20 * zoom}px ${20 * zoom}px`,
          backgroundPosition: `${pan.x}px ${pan.y}px`,
        }}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onContextMenu={handleContextMenu}
        onClick={handleCanvasClick}
      >
        {/* Transform container */}
        <div
          style={{
            transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
            transformOrigin: '0 0',
          }}
        >
          {/* Connections */}
          <svg 
            className="absolute top-0 left-0 w-full h-full pointer-events-none"
            style={{ overflow: 'visible' }}
          >
            {currentGraph?.connections.map(conn => {
              const sourceNode = currentGraph.nodes.find(n => n.id === conn.sourceNodeId);
              const targetNode = currentGraph.nodes.find(n => n.id === conn.targetNodeId);
              if (!sourceNode || !targetNode) return null;
              
              return (
                <ConnectionLine
                  key={conn.id}
                  from={{ x: sourceNode.position.x + 200, y: sourceNode.position.y + 30 }}
                  to={{ x: targetNode.position.x, y: targetNode.position.y + 30 }}
                />
              );
            })}

            {/* Drawing connection */}
            {connectingFrom && (
              <ConnectionLine
                from={{
                  x: currentGraph?.nodes.find(n => n.id === connectingFrom.nodeId)?.position.x! + 200,
                  y: currentGraph?.nodes.find(n => n.id === connectingFrom.nodeId)?.position.y! + 30,
                }}
                to={mousePos}
                dashed
              />
            )}
          </svg>

          {/* Nodes */}
          {currentGraph?.nodes.map(node => (
            <LogicNodeComponent
              key={node.id}
              node={node}
              isSelected={selectedNodeIds.includes(node.id)}
              onSelect={() => selectNodes([node.id])}
              onPositionChange={(pos) => updateLogicNode(node.id, { position: pos })}
              onPortMouseDown={(port, isOutput) => handlePortMouseDown(node.id, port, isOutput)}
              onPortMouseUp={(port, isInput) => handlePortMouseUp(node.id, port, isInput)}
            />
          ))}
        </div>

        {/* Node Menu */}
        {showNodeMenu && (
          <NodeMenu
            position={nodeMenuPos}
            zoom={zoom}
            pan={pan}
            onAddNode={handleAddNode}
            onClose={() => setShowNodeMenu(false)}
          />
        )}
      </div>
    </div>
  );
}

interface ConnectionLineProps {
  from: Position;
  to: Position;
  dashed?: boolean;
}

function ConnectionLine({ from, to, dashed }: ConnectionLineProps) {
  const dx = to.x - from.x;
  const controlX = Math.abs(dx) * 0.5;
  
  const d = `M ${from.x} ${from.y} C ${from.x + controlX} ${from.y}, ${to.x - controlX} ${to.y}, ${to.x} ${to.y}`;
  
  return (
    <path
      d={d}
      fill="none"
      stroke="#e94560"
      strokeWidth={2}
      strokeDasharray={dashed ? '5,5' : undefined}
    />
  );
}

interface LogicNodeComponentProps {
  node: LogicNode;
  isSelected: boolean;
  onSelect: () => void;
  onPositionChange: (pos: Position) => void;
  onPortMouseDown: (port: string, isOutput: boolean) => void;
  onPortMouseUp: (port: string, isInput: boolean) => void;
}

function LogicNodeComponent({
  node,
  isSelected,
  onSelect,
  onPositionChange,
  onPortMouseDown,
  onPortMouseUp
}: LogicNodeComponentProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [dragOffset, setDragOffset] = useState<Position>({ x: 0, y: 0 });

  const handleMouseDown = (e: React.MouseEvent) => {
    e.stopPropagation();
    onSelect();
    setIsDragging(true);
    setDragOffset({
      x: e.clientX - node.position.x,
      y: e.clientY - node.position.y,
    });
  };

  useEffect(() => {
    if (!isDragging) return;

    const handleMouseMove = (e: MouseEvent) => {
      onPositionChange({
        x: e.clientX - dragOffset.x,
        y: e.clientY - dragOffset.y,
      });
    };

    const handleMouseUp = () => {
      setIsDragging(false);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);

    return () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging, dragOffset, onPositionChange]);

  const getNodeColor = () => {
    switch (node.type) {
      case 'event': return 'bg-red-600';
      case 'action': return 'bg-blue-600';
      case 'condition': return 'bg-yellow-600';
      case 'variable': return 'bg-green-600';
      default: return 'bg-arsist-primary';
    }
  };

  const getNodeTitle = () => {
    if (node.eventType) return node.eventType;
    if (node.actionType) return node.actionType;
    return node.type;
  };

  return (
    <div
      className={`absolute rounded-lg shadow-lg ${isSelected ? 'ring-2 ring-arsist-accent' : ''}`}
      style={{
        left: node.position.x,
        top: node.position.y,
        minWidth: 200,
      }}
      onMouseDown={handleMouseDown}
    >
      {/* Header */}
      <div className={`${getNodeColor()} px-3 py-2 rounded-t-lg flex items-center gap-2 cursor-move`}>
        {node.type === 'event' && <Zap size={14} />}
        {node.type === 'action' && <Play size={14} />}
        {node.type === 'condition' && <GitBranch size={14} />}
        <span className="text-sm font-medium">{getNodeTitle()}</span>
      </div>

      {/* Body */}
      <div className="bg-arsist-surface rounded-b-lg p-2">
        <div className="flex justify-between">
          {/* Input ports */}
          <div className="space-y-2">
            {node.inputs?.map((input, i) => (
              <div 
                key={i} 
                className="flex items-center gap-2"
                onMouseUp={() => onPortMouseUp(input, true)}
              >
                <div 
                  className={`node-port ${input === 'exec' ? 'node-port-exec' : ''}`}
                  style={{ cursor: 'pointer' }}
                />
                <span className="text-xs text-arsist-muted">{input}</span>
              </div>
            ))}
          </div>

          {/* Output ports */}
          <div className="space-y-2">
            {node.outputs?.map((output, i) => (
              <div 
                key={i} 
                className="flex items-center gap-2 justify-end"
                onMouseDown={(e) => {
                  e.stopPropagation();
                  onPortMouseDown(output, true);
                }}
              >
                <span className="text-xs text-arsist-muted">{output}</span>
                <div 
                  className={`node-port ${output === 'exec' ? 'node-port-exec' : ''}`}
                  style={{ cursor: 'crosshair' }}
                />
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

interface NodeMenuProps {
  position: Position;
  zoom: number;
  pan: Position;
  onAddNode: (type: string, eventType?: string, actionType?: string) => void;
  onClose: () => void;
}

function NodeMenu({ position, zoom, pan, onAddNode, onClose }: NodeMenuProps) {
  return (
    <div
      className="context-menu"
      style={{
        left: position.x * zoom + pan.x,
        top: position.y * zoom + pan.y,
      }}
      onClick={(e) => e.stopPropagation()}
    >
      <div className="px-3 py-2 text-xs text-arsist-muted border-b border-arsist-primary/30">
        ノード追加
      </div>
      
      <div className="py-1">
        <div className="px-3 py-1 text-xs text-arsist-muted">イベント</div>
        <button className="context-menu-item w-full" onClick={() => onAddNode('event', 'OnStart')}>
          <Play size={14} /> OnStart
        </button>
        <button className="context-menu-item w-full" onClick={() => onAddNode('event', 'OnUpdate')}>
          <RefreshCw size={14} /> OnUpdate
        </button>
        <button className="context-menu-item w-full" onClick={() => onAddNode('event', 'OnGazeEnter')}>
          <Eye size={14} /> OnGazeEnter
        </button>
        <button className="context-menu-item w-full" onClick={() => onAddNode('event', 'OnTap')}>
          <MousePointer size={14} /> OnTap
        </button>
      </div>

      <div className="context-menu-separator" />

      <div className="py-1">
        <div className="px-3 py-1 text-xs text-arsist-muted">アクション</div>
        <button className="context-menu-item w-full" onClick={() => onAddNode('action', undefined, 'Debug.Log')}>
          <Type size={14} /> Debug.Log
        </button>
        <button className="context-menu-item w-full" onClick={() => onAddNode('action', undefined, 'TweenMove')}>
          <ArrowRight size={14} /> Tween Move
        </button>
        <button className="context-menu-item w-full" onClick={() => onAddNode('action', undefined, 'SetActive')}>
          <Box size={14} /> Set Active
        </button>
      </div>

      <div className="context-menu-separator" />

      <div className="py-1">
        <div className="px-3 py-1 text-xs text-arsist-muted">変数</div>
        <button className="context-menu-item w-full" onClick={() => onAddNode('variable')}>
          <Hash size={14} /> Variable
        </button>
      </div>
    </div>
  );
}
