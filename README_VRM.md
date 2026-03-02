# Arsist VRM & 3D Asset Control - Quick Start

Arsist エンジンで VRM モデルと 3D アセットを完全に制御できるようになりました。

## 🚀 クイックスタート

### 1. インストール

```bash
npm install
```

### 2. VRM モデルを追加

1. エディタを起動: `npm run dev`
2. シーンタブを開く
3. 「追加」→「VRM モデル」
4. .vrm ファイルを選択
5. 右パネルで **Asset ID** を設定（例: `avatar`）

### 3. スクリプトで制御

```javascript
// 表情を変える
vrm.setExpression('avatar', 'Joy', 100);

// 手を振る
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);

// 位置を変える
scene.setPosition('avatar', 0, 0, 2);
```

### 4. Python から制御

```python
from python.arsist_controller import ArsistRemoteController

controller = ArsistRemoteController("192.168.1.100")
controller.connect()
controller.set_expression("avatar", "Joy", 100)
controller.wave_hand("avatar")
controller.disconnect()
```

## 📚 ドキュメント

- **[完全使用ガイド](docs/complete-usage-guide.md)** - 詳細な使い方
- **[API リファレンス](docs/3d-asset-api.md)** - scene / vrm API
- **[VRM 統合ガイド](docs/vrm-integration-guide.md)** - VRM の詳細
- **[Python サンプル](docs/samples/08_python_remote_control.md)** - リモート制御

## 🎮 サンプル

- [05_3d_object_control.md](docs/samples/05_3d_object_control.md) - GLB モデル制御
- [06_vrm_avatar_control.md](docs/samples/06_vrm_avatar_control.md) - VRM 基本制御
- [07_vrm_interactive_avatar.md](docs/samples/07_vrm_interactive_avatar.md) - インタラクティブアバター
- [08_python_remote_control.md](docs/samples/08_python_remote_control.md) - Python リモート制御

## ✨ 主な機能

### エディタ
- ✅ VRM リアルタイムプレビュー（`@pixiv/three-vrm`）
- ✅ Asset ID 管理 UI
- ✅ Transform 編集
- ✅ VRM / GLB 自動判別

### スクリプト API
- ✅ `scene.setPosition()` - 位置制御
- ✅ `scene.playAnimation()` - アニメーション
- ✅ `vrm.setExpression()` - 表情制御
- ✅ `vrm.setBoneRotation()` - ボーン制御
- ✅ `vrm.lookAt()` - 視線制御

### Python リモート制御
- ✅ WebSocket サーバー（ポート 8765）
- ✅ シンプルな Python API
- ✅ AI / センサー連携可能
- ✅ リアルタイム制御

### デバイス対応
- ✅ Meta Quest (2/3/3S/Pro)
- ✅ XREAL One / One Pro
- ✅ IL2CPP ビルド対応

## 🔧 技術スタック

**フロントエンド:**
- React + TypeScript
- @pixiv/three-vrm (VRM プレビュー)
- @react-three/fiber (3D レンダリング)

**Unity バックエンド:**
- C# WebSocket サーバー
- SceneWrapper / VRMWrapper (スクリプト API)
- UniVRM (VRM ランタイム)

**Python:**
- websocket-client
- JSON コマンド

## 📦 ファイル構成

```
Arsist/
├── src/
│   ├── renderer/components/viewport/
│   │   └── VRMViewer.tsx          # VRM プレビュー
│   └── shared/types.ts             # 型定義（assetId 追加）
├── UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/
│   ├── Scripting/
│   │   ├── SceneWrapper.cs        # 3D オブジェクト制御
│   │   └── VRMWrapper.cs          # VRM 制御
│   └── Network/
│       └── ArsistWebSocketServer.cs # WebSocket サーバー
├── python/
│   └── arsist_controller.py       # Python クライアント
└── docs/
    ├── complete-usage-guide.md    # 完全ガイド
    ├── 3d-asset-api.md            # API リファレンス
    └── samples/                   # サンプル集
```

## 🎯 使用例

### 感情表現

```javascript
// スクリプト
vrm.setExpression('avatar', 'Joy', 100);      // 喜び
vrm.setExpression('avatar', 'Sorrow', 100);   // 悲しみ
vrm.setExpression('avatar', 'Angry', 100);    // 怒り
```

```python
# Python
controller.set_emotion("avatar", "happy")
controller.set_emotion("avatar", "sad")
controller.set_emotion("avatar", "angry")
```

### ポーズ制御

```javascript
// 手を振る
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);
vrm.setBoneRotation('avatar', 'RightLowerArm', -30, 0, 0);
```

```python
# Python
controller.wave_hand("avatar", right=True)
```

### AI 連携

```python
import openai

# ChatGPT の感情分析結果を VRM に反映
response = openai.ChatCompletion.create(...)
if "[happy]" in response:
    controller.set_emotion("avatar", "happy")
```

## 🐛 トラブルシューティング

**VRM が表示されない:**
- Asset ID が設定されているか確認
- Unity に UniVRM がインポートされているか確認

**Python から接続できない:**
- デバイスと PC が同じネットワークにいるか確認
- IP アドレスを確認: `adb shell ip addr show wlan0`

**スクリプトが動作しない:**
- `scene.exists('avatar')` で存在確認
- Unity Console でエラーログを確認

## 📖 詳細ドキュメント

すべての詳細は [docs/complete-usage-guide.md](docs/complete-usage-guide.md) を参照してください。

## 🤝 サポート

問題が発生した場合:
1. [完全使用ガイド](docs/complete-usage-guide.md) を確認
2. [トラブルシューティング](docs/complete-usage-guide.md#トラブルシューティング) を確認
3. GitHub Issues で報告

---

**すべての機能が実装済みで、すぐに使用可能です！**
