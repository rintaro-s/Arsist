"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.convertSceneToUnity = convertSceneToUnity;
exports.convertUIToUnity = convertUIToUnity;
exports.convertLogicToCode = convertLogicToCode;
exports.generateUnityManifest = generateUnityManifest;
/**
 * シーンデータをUnity用JSONに変換
 */
function convertSceneToUnity(scene) {
    return {
        name: scene.name,
        gameObjects: scene.objects.map(obj => convertObjectToUnity(obj)),
    };
}
function convertObjectToUnity(obj) {
    const components = [];
    // Add mesh components for primitives
    if (obj.type === 'primitive') {
        components.push({
            type: 'MeshFilter',
            properties: {
                mesh: getPrimitiveMeshName(obj.primitiveType),
            },
        });
        components.push({
            type: 'MeshRenderer',
            properties: {
                material: {
                    shader: 'Universal Render Pipeline/Lit',
                    color: hexToRgba(obj.material?.color || '#FFFFFF'),
                    metallic: obj.material?.metallic || 0,
                    smoothness: 1 - (obj.material?.roughness || 0.5),
                },
            },
        });
    }
    // Add model loader component for GLB/GLTF models
    if (obj.type === 'model' && obj.modelPath) {
        components.push({
            type: 'ArsistModelRuntimeLoader',
            properties: {
                modelPath: obj.modelPath,
                destroyAfterLoad: true,
            },
        });
        // **モデルの回転を適用するための補助コンポーネント**
        // (modelLoaderが読み込み後、rotation を設定するため)
        if (obj.transform.rotation.x !== 0 || obj.transform.rotation.y !== 0 || obj.transform.rotation.z !== 0) {
            components.push({
                type: 'ArsistModelRotationApplier',
                properties: {
                    targetRotation: {
                        x: obj.transform.rotation.x,
                        y: obj.transform.rotation.y,
                        z: obj.transform.rotation.z,
                    },
                    applyDelay: 0.5, // モデル読み込み後に遅延適用
                },
            });
        }
    }
    // Add light component
    if (obj.type === 'light') {
        components.push({
            type: 'Light',
            properties: {
                type: 'Point',
                color: hexToRgba(obj.material?.color || '#FFFFFF'),
                intensity: 1,
                range: 10,
            },
        });
    }
    // Add custom components
    for (const comp of obj.components) {
        components.push({
            type: comp.type,
            properties: comp.properties,
        });
    }
    return {
        name: obj.name,
        transform: {
            localPosition: obj.transform.position,
            localRotation: obj.transform.rotation,
            localScale: obj.transform.scale,
        },
        components,
        children: obj.children?.map(child => convertObjectToUnity(child)),
    };
}
function getPrimitiveMeshName(primitiveType) {
    switch (primitiveType) {
        case 'cube': return 'Cube';
        case 'sphere': return 'Sphere';
        case 'plane': return 'Plane';
        case 'cylinder': return 'Cylinder';
        case 'capsule': return 'Capsule';
        default: return 'Cube';
    }
}
function hexToRgba(hex) {
    const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})?$/i.exec(hex);
    return result ? {
        r: parseInt(result[1], 16) / 255,
        g: parseInt(result[2], 16) / 255,
        b: parseInt(result[3], 16) / 255,
        a: result[4] ? parseInt(result[4], 16) / 255 : 1,
    } : { r: 1, g: 1, b: 1, a: 1 };
}
/**
 * UIレイアウトをUnity UI Toolkit/uGUI用に変換
 */
function convertUIToUnity(layout) {
    return {
        name: layout.name,
        root: convertUIElementToUnity(layout.root),
    };
}
function convertUIElementToUnity(element) {
    const result = {
        name: `${element.type}_${element.id.substring(0, 8)}`,
        type: element.type,
        rectTransform: calculateRectTransform(element),
        children: element.children?.map(child => convertUIElementToUnity(child)) || [],
    };
    // Layout Group
    if (element.layout && element.layout !== 'Absolute') {
        result.layoutGroup = {
            type: element.layout === 'FlexRow' ? 'Horizontal' :
                element.layout === 'FlexColumn' ? 'Vertical' : 'Grid',
            spacing: element.style.gap || 0,
            childAlignment: mapAlignment(element.style.justifyContent, element.style.alignItems),
            padding: element.style.padding || { left: 0, right: 0, top: 0, bottom: 0 },
        };
    }
    // Image component (for Panel, Button backgrounds)
    if (element.type === 'Panel' || element.type === 'Button') {
        result.image = {
            color: hexToRgba(element.style.backgroundColor || '#00000000'),
            raycastTarget: element.type === 'Button',
        };
        // Blur effect requires special material
        if (element.style.blur) {
            result.image.material = 'UIBlur';
        }
    }
    // Text component
    if (element.type === 'Text' || element.type === 'Button') {
        result.text = {
            text: element.content || '',
            fontSize: element.style.fontSize || 16,
            color: hexToRgba(element.style.color || '#FFFFFF'),
            alignment: mapTextAlignment(element.style.textAlign),
            fontStyle: element.style.fontWeight === 'bold' ? 'Bold' : 'Normal',
        };
    }
    // Button component
    if (element.type === 'Button') {
        const baseColor = hexToRgba(element.style.backgroundColor || '#E94560');
        result.button = {
            targetGraphic: 'Image',
            colors: {
                normalColor: baseColor,
                highlightedColor: { ...baseColor, a: baseColor.a * 0.8 },
                pressedColor: { ...baseColor, a: baseColor.a * 0.6 },
            },
            onClick: element.events?.find(e => e.type === 'onClick')?.action || '',
        };
    }
    return result;
}
function calculateRectTransform(element) {
    // Default center anchor
    let anchorMin = { x: 0.5, y: 0.5 };
    let anchorMax = { x: 0.5, y: 0.5 };
    let pivot = { x: 0.5, y: 0.5 };
    // Calculate based on anchor style
    if (element.style.anchor) {
        switch (element.style.anchor.horizontal) {
            case 'left':
                anchorMin.x = 0;
                anchorMax.x = 0;
                pivot.x = 0;
                break;
            case 'right':
                anchorMin.x = 1;
                anchorMax.x = 1;
                pivot.x = 1;
                break;
            case 'stretch':
                anchorMin.x = 0;
                anchorMax.x = 1;
                break;
        }
        switch (element.style.anchor.vertical) {
            case 'top':
                anchorMin.y = 1;
                anchorMax.y = 1;
                pivot.y = 1;
                break;
            case 'bottom':
                anchorMin.y = 0;
                anchorMax.y = 0;
                pivot.y = 0;
                break;
            case 'stretch':
                anchorMin.y = 0;
                anchorMax.y = 1;
                break;
        }
    }
    return {
        anchorMin,
        anchorMax,
        pivot,
        sizeDelta: {
            x: typeof element.style.width === 'number' ? element.style.width : 100,
            y: typeof element.style.height === 'number' ? element.style.height : 100,
        },
        anchoredPosition: {
            x: element.style.left || 0,
            y: -(element.style.top || 0), // Unity Y is inverted
        },
    };
}
function mapAlignment(justify, align) {
    const v = align === 'flex-start' ? 'Upper' :
        align === 'flex-end' ? 'Lower' : 'Middle';
    const h = justify === 'flex-start' ? 'Left' :
        justify === 'flex-end' ? 'Right' : 'Center';
    return `${v}${h}`;
}
function mapTextAlignment(align) {
    switch (align) {
        case 'left': return 'Left';
        case 'center': return 'Center';
        case 'right': return 'Right';
        default: return 'Center';
    }
}
/**
 * ロジックグラフをC#コードに変換
 */
function convertLogicToCode(graphs, projectName) {
    let code = `// ==============================================
// Auto-generated by Arsist Engine
// Project: ${projectName}
// Generated: ${new Date().toISOString()}
// Do not edit manually - changes will be overwritten
// ==============================================

using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using DG.Tweening; // DOTween for animations

namespace Arsist.Generated
{
`;
    for (const graph of graphs) {
        code += generateClassFromGraph(graph);
    }
    code += `}
`;
    return code;
}
function generateClassFromGraph(graph) {
    const className = sanitizeIdentifier(graph.name);
    let classCode = `
    /// <summary>
    /// Logic graph: ${graph.name}
    /// </summary>
    public class ${className} : MonoBehaviour
    {
        // Variables
`;
    // Generate variables
    for (const variable of graph.variables || []) {
        const varType = mapVariableType(variable.type);
        const varName = sanitizeIdentifier(variable.name);
        classCode += `        public ${varType} ${varName};\n`;
    }
    classCode += `
        // Event methods
`;
    // Generate event handlers
    const eventNodes = graph.nodes.filter(n => n.type === 'event');
    for (const eventNode of eventNodes) {
        const methodCode = generateEventMethod(eventNode, graph);
        classCode += methodCode;
    }
    // Generate action methods
    classCode += `
        // Action methods
`;
    const actionNodes = graph.nodes.filter(n => n.type === 'action');
    for (const actionNode of actionNodes) {
        if (actionNode.actionType) {
            classCode += generateActionMethod(actionNode);
        }
    }
    classCode += `    }

`;
    return classCode;
}
function generateEventMethod(eventNode, graph) {
    const unityEventName = mapEventToUnity(eventNode.eventType || '');
    if (!unityEventName)
        return '';
    let methodBody = '            // Generated from visual script\n';
    // Find connected actions
    const connections = graph.connections.filter(c => c.sourceNodeId === eventNode.id);
    for (const conn of connections) {
        const targetNode = graph.nodes.find(n => n.id === conn.targetNodeId);
        if (targetNode && targetNode.type === 'action') {
            methodBody += `            ${generateActionCall(targetNode)};\n`;
        }
    }
    return `
        private void ${unityEventName}()
        {
${methodBody}        }

`;
}
function mapEventToUnity(eventType) {
    switch (eventType) {
        case 'OnStart': return 'Start';
        case 'OnUpdate': return 'Update';
        case 'OnGazeEnter': return 'OnGazeEnter';
        case 'OnGazeExit': return 'OnGazeExit';
        case 'OnTap': return 'OnPointerClick';
        default: return '';
    }
}
function generateActionCall(node) {
    switch (node.actionType) {
        case 'Debug.Log':
            const message = node.properties?.message || 'Debug message';
            return `Debug.Log("${message}")`;
        case 'TweenMove':
            const target = node.properties?.target || 'transform';
            const position = node.properties?.position || '{ x: 0, y: 0, z: 0 }';
            const duration = node.properties?.duration || 1;
            return `${target}.DOMove(new Vector3(${position.x}, ${position.y}, ${position.z}), ${duration}f)`;
        case 'SetActive':
            const obj = node.properties?.target || 'gameObject';
            const active = node.properties?.active ?? true;
            return `${obj}.SetActive(${active})`;
        case 'FadeIn':
            return `GetComponent<CanvasGroup>().DOFade(1f, 0.5f)`;
        case 'FadeOut':
            return `GetComponent<CanvasGroup>().DOFade(0f, 0.5f)`;
        default:
            return `// Unknown action: ${node.actionType}`;
    }
}
function generateActionMethod(node) {
    const methodName = `Action_${sanitizeIdentifier(node.actionType || 'Unknown')}`;
    return `
        public void ${methodName}()
        {
            ${generateActionCall(node)};
        }

`;
}
function mapVariableType(type) {
    switch (type) {
        case 'number': return 'float';
        case 'string': return 'string';
        case 'boolean': return 'bool';
        case 'vector3': return 'Vector3';
        case 'object': return 'GameObject';
        default: return 'object';
    }
}
function sanitizeIdentifier(name) {
    return name.replace(/[^a-zA-Z0-9_]/g, '_').replace(/^[0-9]/, '_$&');
}
/**
 * プロジェクト全体をUnityマニフェストに変換
 */
function generateUnityManifest(project) {
    return {
        arsistVersion: '1.0.0',
        projectId: project.id,
        projectName: project.name,
        appType: project.appType,
        targetDevice: project.targetDevice,
        arSettings: project.arSettings,
        uiAuthoring: project.uiAuthoring,
        uiCode: project.uiCode,
        build: {
            packageName: project.buildSettings.packageName,
            version: project.buildSettings.version,
            versionCode: project.buildSettings.versionCode,
            minSdkVersion: project.buildSettings.minSdkVersion,
            targetSdkVersion: project.buildSettings.targetSdkVersion,
        },
        design: {
            defaultFont: project.designSystem.defaultFont,
            primaryColor: project.designSystem.primaryColor,
            secondaryColor: project.designSystem.secondaryColor,
            backgroundColor: project.designSystem.backgroundColor,
            textColor: project.designSystem.textColor,
        },
        scenes: project.scenes.map(s => ({
            id: s.id,
            name: s.name,
            objectCount: s.objects.length,
        })),
        uiLayouts: project.uiLayouts.map(l => ({
            id: l.id,
            name: l.name,
        })),
        logicGraphs: project.logicGraphs.map(g => ({
            id: g.id,
            name: g.name,
            nodeCount: g.nodes.length,
        })),
        generatedAt: new Date().toISOString(),
    };
}
//# sourceMappingURL=UnityBridge.js.map