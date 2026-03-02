/**
 * Arsist Engine - 共有型定義
 */
export type ProjectTemplate = '3d_ar_scene' | '2d_floating_screen' | 'head_locked_hud';
export type TrackingMode = '6dof' | '3dof' | 'head_locked';
export type PresentationMode = 'world_anchored' | 'floating_screen' | 'head_locked_hud';
export type UIAuthoringMode = 'visual' | 'code' | 'hybrid';
export type UISyncMode = 'two-way' | 'visual-to-code' | 'code-to-visual' | 'none';
export type DataSourceMode = 'polling' | 'event';
export type DataSourceType = 'XR_Tracker' | 'XR_HandPose' | 'Device_Status' | 'Location_Provider' | 'REST_Client' | 'WebSocket_Stream' | 'MQTT_Subscriber' | 'System_Clock' | 'Voice_Recognition' | 'Microphone_Level';
export type TransformType = 'Formula' | 'Clamper' | 'Remap' | 'Smoother' | 'Comparator' | 'Threshold' | 'State_Mapper' | 'String_Template' | 'Time_Formatter' | 'History_Buffer' | 'Accumulator';
export interface DataSourceDefinition {
    id: string;
    type: DataSourceType;
    mode: DataSourceMode;
    storeAs: string;
    updateRate?: number;
    parameters?: Record<string, any>;
}
export interface TransformDefinition {
    id: string;
    type: TransformType;
    inputs: string[];
    storeAs: string;
    expression?: string;
    updateRate?: number;
    parameters?: Record<string, any>;
}
export interface DataFlowDefinition {
    dataSources: DataSourceDefinition[];
    transforms: TransformDefinition[];
}
export interface ARSettings {
    trackingMode: TrackingMode;
    presentationMode: PresentationMode;
    worldScale: number;
    defaultDepth: number;
    floatingScreen?: {
        width: number;
        height: number;
        distance: number;
        lockToGaze: boolean;
    };
    enableRemoteControl?: boolean;
    remoteControlPort?: number;
    remoteControlPassword?: string;
}
export interface UICodeBundle {
    html: string;
    css: string;
    js: string;
    lastSyncedFrom: 'visual' | 'code' | 'none';
}
export interface UIAuthoringSettings {
    mode: UIAuthoringMode;
    syncMode: UISyncMode;
}
export interface ArsistProject {
    id: string;
    name: string;
    version: string;
    createdAt: string;
    updatedAt: string;
    appType: string;
    targetDevice: string;
    arSettings: ARSettings;
    uiAuthoring: UIAuthoringSettings;
    uiCode: UICodeBundle;
    designSystem: DesignSystem;
    dataFlow: DataFlowDefinition;
    scenes: SceneData[];
    uiLayouts: UILayoutData[];
    logicGraphs: LogicGraphData[];
    buildSettings: BuildSettings;
}
export interface DesignSystem {
    defaultFont: string;
    primaryColor: string;
    secondaryColor: string;
    backgroundColor: string;
    textColor: string;
}
export interface BuildSettings {
    packageName: string;
    version: string;
    versionCode: number;
    minSdkVersion: number;
    targetSdkVersion: number;
    remoteInput?: RemoteInputSettings;
}
export interface RemoteInputSettings {
    udp?: {
        enabled: boolean;
        port: number;
    };
    tcp?: {
        enabled: boolean;
        port: number;
    };
    /**
     * 受け付けるイベント名。空/未指定なら全受理（開発用）。
     * 本番では明示指定を推奨。
     */
    allowedEvents?: string[];
}
export interface SceneData {
    id: string;
    name: string;
    objects: SceneObject[];
}
export interface SceneObject {
    id: string;
    name: string;
    type: 'primitive' | 'model' | 'light' | 'camera' | 'empty' | 'ui_surface';
    primitiveType?: 'cube' | 'sphere' | 'plane' | 'cylinder' | 'capsule';
    modelPath?: string;
    uiSurface?: UISurfaceSettings;
    transform: Transform;
    material?: MaterialData;
    components: ComponentData[];
    children?: SceneObject[];
}
export interface Transform {
    position: Vector3;
    rotation: Vector3;
    scale: Vector3;
}
export interface Vector3 {
    x: number;
    y: number;
    z: number;
}
export interface MaterialData {
    color: string;
    metallic?: number;
    roughness?: number;
    texture?: string;
    emissive?: string;
    emissiveIntensity?: number;
}
export interface ComponentData {
    type: string;
    properties: Record<string, any>;
}
export interface UILayoutData {
    id: string;
    name: string;
    scope?: 'uhd' | 'surface';
    root: UIElement;
}
export interface UIElement {
    id: string;
    type: 'Panel' | 'Text' | 'Button' | 'Image' | 'Video' | 'Slider' | 'Input' | 'ScrollView' | 'List';
    content?: string;
    assetPath?: string;
    bind?: UIBinding;
    layout?: 'FlexRow' | 'FlexColumn' | 'Grid' | 'Absolute';
    style: UIStyle;
    children: UIElement[];
    events?: UIEvent[];
}
export interface UIBinding {
    key: string;
    format?: string;
}
export interface UISurfaceSettings {
    layoutId: string;
    width: number;
    height: number;
    pixelsPerUnit?: number;
    headLocked?: boolean;
    distance?: number;
}
export interface UIStyle {
    width?: number | string;
    height?: number | string;
    minWidth?: number;
    minHeight?: number;
    maxWidth?: number;
    maxHeight?: number;
    margin?: Spacing;
    padding?: Spacing;
    flexDirection?: 'row' | 'column';
    justifyContent?: 'flex-start' | 'center' | 'flex-end' | 'space-between' | 'space-around';
    alignItems?: 'flex-start' | 'center' | 'flex-end' | 'stretch';
    gap?: number;
    backgroundColor?: string;
    color?: string;
    borderRadius?: number;
    borderWidth?: number;
    borderColor?: string;
    blur?: number;
    opacity?: number;
    shadow?: ShadowStyle;
    fontSize?: number;
    fontWeight?: 'normal' | 'bold' | '100' | '200' | '300' | '400' | '500' | '600' | '700' | '800' | '900';
    textAlign?: 'left' | 'center' | 'right';
    anchor?: {
        horizontal: 'left' | 'center' | 'right' | 'stretch';
        vertical: 'top' | 'center' | 'bottom' | 'stretch';
    };
    top?: number;
    right?: number;
    bottom?: number;
    left?: number;
}
export interface Spacing {
    top: number;
    right: number;
    bottom: number;
    left: number;
}
export interface ShadowStyle {
    offsetX: number;
    offsetY: number;
    blur: number;
    color: string;
}
export interface UIEvent {
    type: 'onClick' | 'onHover' | 'onValueChanged' | 'onSubmit';
    action: string;
    parameters?: Record<string, any>;
}
export interface LogicGraphData {
    id: string;
    name: string;
    nodes: LogicNode[];
    connections: LogicConnection[];
    variables?: LogicVariable[];
}
export interface LogicNode {
    id: string;
    type: 'event' | 'action' | 'condition' | 'variable' | 'math' | 'string' | 'object';
    eventType?: string;
    actionType?: string;
    position: {
        x: number;
        y: number;
    };
    inputs?: string[];
    outputs?: string[];
    properties?: Record<string, any>;
}
export interface LogicConnection {
    id: string;
    sourceNodeId: string;
    sourcePort: string;
    targetNodeId: string;
    targetPort: string;
}
export interface LogicVariable {
    id: string;
    name: string;
    type: 'number' | 'string' | 'boolean' | 'vector3' | 'object';
    defaultValue: any;
}
export interface EditorState {
    currentView: 'scene' | 'ui' | 'logic';
    selectedObjectId: string | null;
    selectedUIElementId: string | null;
    selectedNodeIds: string[];
    viewportCamera: {
        position: Vector3;
        rotation: Vector3;
        zoom: number;
    };
    gridEnabled: boolean;
    snapEnabled: boolean;
    snapSize: number;
}
export interface BuildConfig {
    targetDevice: string;
    buildTarget: 'Android' | 'iOS' | 'Windows' | 'MacOS';
    outputPath: string;
    developmentBuild: boolean;
    compressionMethod: 'LZ4' | 'LZ4HC' | 'None';
    scriptingBackend: 'Mono' | 'IL2CPP';
}
export interface BuildResult {
    success: boolean;
    outputPath?: string;
    error?: string;
    warnings?: string[];
    buildTime?: number;
    fileSize?: number;
}
export interface LayoutSettings {
    leftPanelWidth: number;
    rightPanelWidth: number;
    bottomPanelHeight: number;
}
export interface EngineSettings {
    unityPath: string;
    unityVersion: string;
    defaultOutputPath: string;
    layoutSettings: LayoutSettings;
    theme: 'dark' | 'light';
}
//# sourceMappingURL=types.d.ts.map