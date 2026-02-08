import type { ArsistProject, ProjectTemplate } from '../../shared/types';
export interface CreateProjectOptions {
    name: string;
    path: string;
    template: ProjectTemplate;
    targetDevice: string;
    trackingMode?: string;
    presentationMode?: string;
}
export interface ExportOptions {
    format: 'unity' | 'json' | 'yaml';
    outputPath: string;
    includeAssets: boolean;
}
export declare class ProjectManager {
    private currentProject;
    private projectPath;
    createProject(options: CreateProjectOptions): Promise<{
        success: boolean;
        project?: ArsistProject;
        error?: string;
    }>;
    loadProject(projectPath: string): Promise<{
        success: boolean;
        project?: ArsistProject;
        error?: string;
    }>;
    saveProject(data: Partial<ArsistProject>): Promise<{
        success: boolean;
        error?: string;
    }>;
    exportProject(options: ExportOptions): Promise<{
        success: boolean;
        outputPath?: string;
        error?: string;
    }>;
    getCurrentProject(): ArsistProject | null;
    getProjectPath(): string | null;
    /** 旧テンプレート名を新名に変換（後方互換） */
    private migrateAppType;
    private createInitialScene;
    private createInitialUI;
    private createInitialDataFlow;
    private createARSettings;
    private loadScenes;
    private loadUILayouts;
    private generateUnityManifest;
}
//# sourceMappingURL=ProjectManager.d.ts.map