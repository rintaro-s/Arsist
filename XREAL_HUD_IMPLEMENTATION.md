# XREAL HUD Implementation Guide

## 実装アーキテクチャ: WebView → RenderTexture → Unity Canvas/Quad

### パイプライン概要
```
[HTML Content]
    ↓
[Android WebView (画面外)]
    ↓ (30fps Bitmap Capture)
[Texture2D]
    ↓ (Graphics.Blit)
[RenderTexture]
    ↓
[Unity Canvas RawImage / 3D Object Material]
    ↓
[XREAL視界内HUD]
```

## 主要コンポーネント

### 1. ArsistWebViewManager.cs
**役割**: WebViewをRenderTextureにキャプチャ
- Android WebViewを画面外に配置（leftMargin: -10000）
- 30fpsでBitmapキャプチャ（目への負担軽減）
- UnityメインスレッドでTexture2D更新
- Graphics.BlitでRenderTextureに転送

**最適化**:
- `LAYER_TYPE_HARDWARE` で描画パフォーマンス向上
- キャプチャ間隔: 0.033秒（33ms）
- 非同期処理でUI応答性維持

### 2. ArsistHUD.cs
**役割**: Head Lock HUD表示
- カメラ追従（Head Lock）
- 推奨距離: 0.7-1.2m
- 視野中央やや下配置（hudOffset）
- スムーズ追従（followSmoothness調整可能）

**XREALPlugin連携**:
```csharp
XREALPlugin.OnTrackingTypeChanged += OnXREALTrackingTypeChanged;
var currentMode = XREALPlugin.GetTrackingType();
bool supports6DOF = XREALPlugin.IsHMDFeatureSupported(...);
```

**3DOF/6DOF自動調整**:
- 3DOFモード: 距離0.8m（近め）
- 6DOFモード: 距離1.0m（標準）

### 3. ArsistWorldCanvas.cs
**役割**: 3D空間固定HTML Canvas
- Cube, Quad, Planeなど任意の3Dオブジェクトに貼り付け
- RenderTextureをMaterialのmainTextureに設定
- `Unlit/Texture`シェーダーで描画

**使用方法**:
```csharp
// Quadを作成
var quad = ArsistWorldCanvas.CreateQuad(
    position: new Vector3(0, 1, 2),
    rotation: Quaternion.identity,
    size: new Vector2(2f, 1f),
    textureSize: new Vector2(1920, 1080)
);
quad.LoadHTML("<html>...</html>");

// Cubeを作成
var cube = ArsistWorldCanvas.CreateCube(
    position: new Vector3(0, 1, 2),
    rotation: Quaternion.identity,
    size: new Vector3(0.5f, 0.5f, 0.5f),
    textureSize: new Vector2(1920, 1080)
);
cube.LoadURL("https://example.com");

// 既存オブジェクトに追加
var worldCanvas = ArsistWorldCanvas.AttachTo3DObject(cubeObject, new Vector2(1920, 1080));
worldCanvas.LoadHTML("<html>...</html>");
```

### 4. ArsistHtmlCanvas3DManager.cs
**役割**: Arsist Engine API経由での3D Canvas管理
```csharp
var manager = ArsistHtmlCanvas3DManager.Instance;

// Quad作成
manager.CreateHtmlQuad(
    id: "myCanvas",
    position: new Vector3(0, 1, 2),
    rotation: new Vector3(0, 0, 0),
    size: new Vector2(2f, 1f),
    htmlContent: "<html>...</html>"
);

// Cube作成
manager.CreateHtmlCube(
    id: "myCube",
    position: new Vector3(0, 1, 2),
    rotation: new Vector3(0, 45, 0),
    size: new Vector3(0.5f, 0.5f, 0.5f),
    htmlContent: "<html>...</html>"
);
```

## Beam Pro入力対応

### XR Interaction Toolkit連携
- `TrackedDeviceGraphicRaycaster`でコントローラー/マウス入力を受付
- XR UI EventSystemで自動処理
- Unity標準のUI EventSystem（onClick等）がそのまま動作

### 実装済み機能
✅ Head Lock（カメラ追従）
✅ 30fps更新（目への負担軽減）
✅ 3D空間配置（Cube/Quad/任意のMesh）
✅ Beam Pro コントローラー入力
✅ マウス入力
✅ RenderTexture パイプライン
✅ Editor プレビュー（ダミー表示）

## パフォーマンスチューニング

### キャプチャ頻度調整
```csharp
[SerializeField] private float captureInterval = 0.033f; // 30fps
```
- 30fps (0.033s): 推奨（バッテリー・パフォーマンスバランス）
- 60fps (0.016s): 高負荷、滑らか
- 15fps (0.066s): 低負荷、カクつく可能性

### テクスチャ解像度
```csharp
[SerializeField] private int textureWidth = 1920;
[SerializeField] private int textureHeight = 1080;
```
- FHD (1920x1080): 推奨
- HD (1280x720): 軽量、文字読みづらい
- UHD (3840x2160): 高負荷、高精細

### スムーズ追従
```csharp
[SerializeField] private float followSmoothness = 0.15f;
```
- 0.0: 即座に追従（カクつく可能性）
- 0.15: 適度なスムーズさ（推奨）
- 0.5: かなり滑らか（遅延感）
- 1.0: 追従なし（固定）

## トラブルシューティング

### HUDが表示されない
1. Main Cameraの`Tag`が`MainCamera`か確認
2. XR Origin > Camera Offset > Main Cameraの階層確認
3. Clear Flags = `Solid Color`, Background = `Black (0,0,0,0)`

### WebViewが真っ黒
1. `captureInterval`を長めに（0.1秒）して再試行
2. `LAYER_TYPE_HARDWARE` → `LAYER_TYPE_SOFTWARE` に変更
3. Androidのハードウェアアクセラレーション設定確認

### コントローラー入力が効かない
1. `TrackedDeviceGraphicRaycaster`がCanvasに付いているか確認
2. XR UI EventSystemがシーンにあるか確認
3. Colliderが3Dオブジェクトに付いているか確認

### パフォーマンスが悪い
1. テクスチャ解像度を下げる（1280x720）
2. captureIntervalを長くする（0.05秒 = 20fps）
3. 複数のHTML Canvasを同時表示しない

## 制限事項
- WebViewはAndroidのみ（Editor/iOSはダミー表示）
- キャプチャ方式のため若干の遅延あり（30-50ms程度）
- 動画再生は負荷が高い（非推奨）
- CSSアニメーションはキャプチャ頻度に依存
