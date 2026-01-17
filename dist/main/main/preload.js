"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
/**
 * Arsist Engine - Preload Script
 * レンダラープロセスとメインプロセスの安全な橋渡し
 */
const electron_1 = require("electron");
// API定義
const electronAPI = {
    // プロジェクト管理
    project: {
        create: (options) => electron_1.ipcRenderer.invoke('project:create', options),
        load: (projectPath) => electron_1.ipcRenderer.invoke('project:load', projectPath),
        save: (data) => electron_1.ipcRenderer.invoke('project:save', data),
        export: (options) => electron_1.ipcRenderer.invoke('project:export', options),
    },
    // Unity連携
    unity: {
        setPath: (unityPath) => electron_1.ipcRenderer.invoke('unity:set-path', unityPath),
        getPath: () => electron_1.ipcRenderer.invoke('unity:get-path'),
        build: (config) => electron_1.ipcRenderer.invoke('unity:build', config),
        validate: () => electron_1.ipcRenderer.invoke('unity:validate'),
        onBuildProgress: (callback) => {
            electron_1.ipcRenderer.on('unity:build-progress', (_, progress) => callback(progress));
        },
        onBuildLog: (callback) => {
            electron_1.ipcRenderer.on('unity:build-log', (_, log) => callback(log));
        },
    },
    // アダプター（SDKパッチ）管理
    adapters: {
        list: () => electron_1.ipcRenderer.invoke('adapters:list'),
        get: (adapterId) => electron_1.ipcRenderer.invoke('adapters:get', adapterId),
        applyPatch: (adapterId, projectPath) => electron_1.ipcRenderer.invoke('adapters:apply-patch', adapterId, projectPath),
    },
    // ファイルシステム
    fs: {
        readFile: (filePath) => electron_1.ipcRenderer.invoke('fs:read-file', filePath),
        writeFile: (filePath, content) => electron_1.ipcRenderer.invoke('fs:write-file', filePath, content),
        selectDirectory: () => electron_1.ipcRenderer.invoke('fs:select-directory'),
        selectFile: (filters) => electron_1.ipcRenderer.invoke('fs:select-file', filters),
    },
    // 設定ストア
    store: {
        get: (key) => electron_1.ipcRenderer.invoke('store:get', key),
        set: (key, value) => electron_1.ipcRenderer.invoke('store:set', key, value),
    },
    // ウィンドウ操作
    window: {
        minimize: () => electron_1.ipcRenderer.invoke('window:minimize'),
        maximize: () => electron_1.ipcRenderer.invoke('window:maximize'),
        close: () => electron_1.ipcRenderer.invoke('window:close'),
    },
    // メニューイベント
    menu: {
        onNewProject: (callback) => {
            electron_1.ipcRenderer.on('menu:new-project', () => callback());
        },
        onSave: (callback) => {
            electron_1.ipcRenderer.on('menu:save', () => callback());
        },
        onSaveAs: (callback) => {
            electron_1.ipcRenderer.on('menu:save-as', () => callback());
        },
        onBuildSettings: (callback) => {
            electron_1.ipcRenderer.on('menu:build-settings', () => callback());
        },
        onBuild: (callback) => {
            electron_1.ipcRenderer.on('menu:build', () => callback());
        },
        onSettings: (callback) => {
            electron_1.ipcRenderer.on('menu:settings', () => callback());
        },
        onDelete: (callback) => {
            electron_1.ipcRenderer.on('menu:delete', () => callback());
        },
        onViewChange: (callback) => {
            electron_1.ipcRenderer.on('menu:view', (_, view) => callback(view));
        },
        onProjectOpen: (callback) => {
            electron_1.ipcRenderer.on('project:open', (_, path) => callback(path));
        },
    },
};
// コンテキストブリッジでAPIを公開
electron_1.contextBridge.exposeInMainWorld('electronAPI', electronAPI);
//# sourceMappingURL=preload.js.map