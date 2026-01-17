# XREAL One Adapter for Arsist Engine

XREAL One ARグラス用のデバイスアダプターです。

## 概要

このアダプターは、ArsistエンジンでXREAL One/One Pro向けのARアプリケーションをビルドするために必要な設定とパッチを提供します。

## 対応デバイス

- XREAL One
- XREAL One Pro
- (XREAL Air シリーズも基本的な互換性あり)

## 必要なSDK

- **XREAL SDK**: 3.1.0以上
- **Unity**: 2022.3.20f1 LTS以上
- **AR Foundation**: 5.0.0以上
- **XR Interaction Toolkit**: 2.5.0以上
- **OpenXR Plugin**: 1.9.0以上

## インストール

1. Arsistエンジンのアダプター管理画面でXREAL Oneを選択
2. 「アダプターをインストール」をクリック
3. Unityプロジェクトにパッチが自動適用されます

## ファイル構成

```
XREAL_One/
├── adapter.json        # アダプター設定ファイル
├── XrealBuildPatcher.cs # ビルドパッチャー
├── AndroidManifest.xml  # Androidマニフェストテンプレート
└── README.md           # このファイル
```

## 適用されるパッチ

### Player Settings
- **アーキテクチャ**: ARM64のみ
- **スクリプトバックエンド**: IL2CPP
- **グラフィックスAPI**: OpenGLES3 (優先) / Vulkan
- **カラースペース**: Linear
- **画面向き**: 横向き固定

### Quality Settings
- **アンチエイリアシング**: MSAAx4
- **シャドウ距離**: 20m
- **ターゲットフレームレート**: 60fps

### XR Configuration
- OpenXR Loaderの自動設定
- XR Interaction Toolkitの初期化
- 視線/レイインタラクションの設定

## ディスプレイ仕様

| 項目 | 値 |
|------|-----|
| 解像度 | 1920x1080 (片目) |
| 視野角 | 50° (水平) / 28° (垂直) |
| リフレッシュレート | 60Hz / 90Hz |
| カラー深度 | 24bit |

## 対応機能

| 機能 | サポート |
|------|---------|
| 6DoFトラッキング | ✅ |
| 平面検出 | ✅ |
| 画像トラッキング | ✅ |
| 空間マッピング | ✅ |
| ジェスチャー認識 | ✅ |
| ハンドトラッキング | ❌ |
| フェイストラッキング | ❌ |
| 音声入力 | ❌ |

## 使用方法

### Arsistエディタから

1. プロジェクトを作成/開く
2. ビルド設定で「XREAL One」を選択
3. 「ビルド」をクリック

### コマンドラインから

```bash
# Arsist CLIでビルド
arsist build --device xreal-one --output ./build/

# Unity直接ビルド
unity -batchmode -quit \
  -executeMethod Arsist.Builder.ArsistBuildPipeline.BuildFromCLI \
  -targetDevice xreal-one \
  -outputPath ./build/
```

## パフォーマンス推奨事項

1. **ドローコール**: 100以下を維持
2. **ポリゴン数**: シーン全体で10万以下
3. **テクスチャ**: ASTC圧縮使用、最大2048x2048
4. **動的ライト**: 最小限に
5. **透明オブジェクト**: 減らす
6. **Single Pass Stereo Rendering**: 有効化

## トラブルシューティング

### ビルドが失敗する

- XREAL SDKがインストールされているか確認
- Unity バージョンが2022.3.20f1以上か確認
- Android SDK/NDKのパスが設定されているか確認

### アプリが起動しない

- APKが署名されているか確認
- minSdkVersionが29以上か確認
- XREAL Oneのファームウェアが最新か確認

### トラッキングが不安定

- 照明条件を改善
- テクスチャの少ない壁を避ける
- カメラの汚れを確認

## ライセンス

MIT License

## サポート

- 問題報告: https://github.com/arsist/adapters/issues
- ドキュメント: https://arsist.dev/docs/adapters/xreal-one
