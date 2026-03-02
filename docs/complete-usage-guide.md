# Arsist 3Dアセット・VRM 完全使用ガイド

このガイドでは、Arsist エディタで 3D アセットと VRM モデルを使用する完全なワークフローを説明します。

---

## 目次

1. [プロジェクトのセットアップ](#プロジェクトのセットアップ)
2. [3D モデルのインポート](#3dモデルのインポート)
3. [VRM モデルのインポート](#vrmモデルのインポート)
4. [エディタでの配置と編集](#エディタでの配置と編集)
5. [Asset ID の設定](#asset-idの設定)
6. [スクリプトからの制御](#スクリプトからの制御)
7. [Python リモートコントロール](#pythonリモートコントロール)
8. [ビルドとデプロイ](#ビルドとデプロイ)

---

## プロジェクトのセットアップ

### 1. 依存関係のインストール

```bash
cd Arsist
npm install
```

これにより `@pixiv/three-vrm` を含むすべての依存関係がインストールされます。

### 2. エディタの起動

```bash
npm run dev
```

### 3. 新規プロジェクトの作成

1. 「新規プロジェクト」をクリック
2. プロジェクト名を入力（例: `MyVRMProject`）
3. テンプレートを選択（例: `3d_ar_scene`）
4. ターゲットデバイスを選択（`Quest` または `XREAL One`）
5. 「作成」をクリック

---

## 3D モデルのインポート

### GLB/GLTF モデルの追加

1. **シーンタブ**を開く
2. ツールバーの「追加」メニューから「モデル」を選択
3. ファイル選択ダイアログで `.glb` または `.gltf` ファイルを選択
4. モデルがシーンに追加されます

### サポートされる形式

- **GLB** (推奨): バイナリ形式、テクスチャ埋め込み
- **GLTF**: JSON 形式、外部テクスチャ参照

### モデルの配置

- **マウス操作**:
  - 左ドラッグ: カメラ回転
  - 右ドラッグ: パン
  - スクロール: ズーム
  
- **トランスフォーム**:
  - `W`: 移動モード
  - `E`: 回転モード
  - `R`: スケールモード

---

## VRM モデルのインポート

### VRM ファイルの追加

1. **シーンタブ**を開く
2. ツールバーの「追加」メニューから「VRM モデル」を選択
3. `.vrm` ファイルを選択
4. VRM モデルがシーンに追加され、自動的にプレビューされます

### VRM プレビュー機能

エディタは `@pixiv/three-vrm` を使用して VRM をリアルタイムプレビューします：

- ✅ VRM 0.x および VRM 1.0 対応
- ✅ BlendShape（表情）のプレビュー
- ✅ Humanoid ボーンの表示
- ✅ MToon シェーダーのプレビュー
- ✅ SpringBone の動作確認

### VRM モデルの推奨仕様

**Quest 向け:**
- ポリゴン数: 10,000 〜 30,000
- テクスチャ: 2048x2048 以下
- BlendShape: 必要最小限（20個以下推奨）

**XREAL 向け:**
- ポリゴン数: 30,000 〜 50,000
- テクスチャ: 2048x2048 以下
- BlendShape: 30個程度まで

---

## エディタでの配置と編集

### オブジェクトの選択

- シーンビューでオブジェクトをクリック
- 左パネルのヒエラルキーから選択

### トランスフォームの編集

**ギズモ操作:**
- 移動: 軸をドラッグ
- 回転: 円をドラッグ
- スケール: ハンドルをドラッグ

**数値入力:**
右パネルの「Transform」セクションで正確な値を入力

```
Position: X: 0.0  Y: 1.5  Z: 2.0
Rotation: X: 0.0  Y: 180.0  Z: 0.0
Scale:    X: 1.0  Y: 1.0  Z: 1.0
```

### マテリアルの編集

右パネルの「Material」セクション:
- Color: カラーピッカーで色を選択
- Metallic: 金属感（0.0 〜 1.0）
- Roughness: 粗さ（0.0 〜 1.0）

---

## Asset ID の設定

### Asset ID とは

スクリプトや Python から 3D オブジェクトを識別するための一意な ID です。

### 設定方法

1. オブジェクトを選択
2. 右パネルの「Asset ID」フィールドに ID を入力
   - 例: `avatar`, `robot_01`, `main_menu`
3. ID は英数字とアンダースコアのみ使用可能

### 命名規則（推奨）

```
avatar          - メインキャラクター
npc_shopkeeper  - NPC（店主）
enemy_01        - 敵キャラクター1
ui_main_menu    - メインメニュー
prop_table      - 小道具（テーブル）
```

### 注意事項

- **一意性**: 同じ ID を複数のオブジェクトに設定しないでください
- **わかりやすさ**: 後で見てもわかる名前をつける
- **スペース禁止**: スペースの代わりにアンダースコアを使用

---

## スクリプトからの制御

### スクリプトエディタを開く

1. ツールバーの「スクリプト」タブをクリック（または `F4`）
2. 「新規スクリプト」をクリック
3. スクリプト名を入力（例: `VRM Controller`）

### 基本的な VRM 制御

```javascript
// onStart トリガー
// アバターを目の前に配置
scene.setPosition('avatar', 0, 0, 2);
scene.setRotation('avatar', 0, 180, 0);

// 笑顔にする
vrm.setExpression('avatar', 'Joy', 100);

log('VRM を初期化しました');
```

### 表情の変更（ボタン連携）

```javascript
// event トリガー: button_happy
vrm.resetExpressions('avatar');
vrm.setExpression('avatar', 'Joy', 100);
```

### ボーン操作（手を振る）

```javascript
// event トリガー: button_wave
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);
vrm.setBoneRotation('avatar', 'RightLowerArm', -30, 0, 0);
```

### アニメーション（毎フレーム更新）

```javascript
// onUpdate トリガー
// 時間に応じて手を振る
var time = Date.now() / 1000;
var angle = Math.sin(time * 3) * 30;
vrm.rotateBone('avatar', 'RightHand', 0, angle, 0);
```

---

## Python リモートコントロール

### セットアップ

デバイスと PC を同じネットワークに接続し、デバイスの IP アドレスを確認します。

### Python クライアント

```python
from arsist_controller import ArsistRemoteController

# デバイスに接続
controller = ArsistRemoteController("192.168.1.100")
controller.connect()

# 表情を変える
controller.set_expression("avatar", "Joy", 100)

# 手を振る
controller.set_bone_rotation("avatar", "RightUpperArm", 0, 0, -90)

# 切断
controller.disconnect()
```

詳細は [08_python_remote_control.md](samples/08_python_remote_control.md) を参照してください。

---

## ビルドとデプロイ

### 1. Unity のセットアップ

**UniVRM のインポート（VRM 使用時のみ）:**

1. Unity エディタを開く
2. `Assets > Import Package > Custom Package`
3. `sdk/UniVRM-0.131.0_3b99.unitypackage` を選択
4. すべてインポート

### 2. プロジェクトのビルド

1. Arsist エディタで「ビルド」メニューを開く
2. ターゲットデバイスを選択
   - **Meta Quest**: Quest 2/3/3S/Pro
   - **XREAL One**: XREAL One/One Pro
3. 出力先を指定
4. 「ビルド開始」をクリック

### 3. ビルド設定の確認

**Quest:**
- Scripting Backend: IL2CPP
- API Level: 32
- Graphics API: Vulkan

**XREAL:**
- Scripting Backend: IL2CPP
- API Level: 29
- Graphics API: OpenGLES3

### 4. デバイスへのインストール

**Quest:**
```bash
adb install -r path/to/output.apk
```

**XREAL:**
```bash
adb install -r path/to/output.apk
```

### 5. 動作確認

1. デバイスでアプリを起動
2. VRM モデルが表示されることを確認
3. スクリプトが正常に動作することを確認
4. Python から接続できることを確認

---

## トラブルシューティング

### VRM が表示されない

**原因1: Asset ID が設定されていない**
- 解決: 右パネルで Asset ID を設定

**原因2: VRM ファイルが破損している**
- 解決: VRoid Studio で再エクスポート

**原因3: UniVRM がインポートされていない**
- 解決: Unity に UniVRM をインポート

### スクリプトが動作しない

**原因1: Asset ID が間違っている**
```javascript
// 存在確認
if (scene.exists('avatar')) {
  log('avatar が見つかりました');
} else {
  error('avatar が見つかりません');
}
```

**原因2: トリガーが設定されていない**
- 解決: スクリプトのトリガータイプを確認

### Python から接続できない

**原因1: ネットワークが異なる**
- 解決: デバイスと PC を同じ Wi-Fi に接続

**原因2: IP アドレスが間違っている**
- 解決: デバイスの設定で IP アドレスを確認

**原因3: ポートが開いていない**
- 解決: ファイアウォール設定を確認

---

## ベストプラクティス

### パフォーマンス最適化

1. **ポリゴン数を抑える**: Quest は 30,000 以下推奨
2. **テクスチャを圧縮**: ASTC 形式を使用
3. **BlendShape を削減**: 使わない表情は削除
4. **ドローコールを減らす**: マテリアルを統合

### Asset ID の管理

1. **命名規則を統一**: チーム内で規則を決める
2. **ドキュメント化**: Asset ID のリストを作成
3. **プレフィックスを使用**: `vrm_`, `ui_`, `prop_` など

### スクリプトの整理

1. **機能ごとに分割**: 1つのスクリプトに1つの機能
2. **コメントを書く**: 後で見てもわかるように
3. **エラーハンドリング**: `exists()` で存在確認

---

## 参考リンク

- [3D Asset API リファレンス](3d-asset-api.md)
- [VRM 統合ガイド](vrm-integration-guide.md)
- [スクリプティング API](scripting-api.md)
- [Python リモートコントロール](samples/08_python_remote_control.md)
- [サンプル集](samples/)

---

## サポート

問題が発生した場合は、以下を確認してください：

1. Unity Console のエラーログ
2. Android Logcat（`adb logcat | grep Arsist`）
3. ブラウザの開発者ツール（エディタ）

それでも解決しない場合は、GitHub Issues で報告してください。
