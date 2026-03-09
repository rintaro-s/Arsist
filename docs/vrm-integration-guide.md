# VRMモデル統合ガイド

このガイドでは、ArsistプロジェクトでVRMモデルを使用する方法を説明します。

---

## 概要

Arsistは以下の2つの方法でVRMモデルをサポートします（**Unity バックエンドでの実機動作には UniVRM のインポートが必須**）：

1. **フロントエンド（エディタ）**: `@pixiv/three-vrm` を使用してVRMをプレビュー
2. **Unity バックエンド**: `UniVRM` を使用してVRMをビルドに含める

---

## フロントエンド: VRMプレビュー

### 依存関係

`@pixiv/three-vrm` はすでに `package.json` に追加されています。

```bash
npm install
```

### 使用方法

```typescript
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { VRMLoaderPlugin } from '@pixiv/three-vrm';

// GLTFLoaderを作成
const loader = new GLTFLoader();

// VRMLoaderPluginを登録
loader.register((parser) => {
  return new VRMLoaderPlugin(parser);
});

// VRMファイルをロード
loader.load(
  '/path/to/model.vrm',
  (gltf) => {
    const vrm = gltf.userData.vrm;
    scene.add(vrm.scene);
    console.log('VRM loaded:', vrm);
  },
  (progress) => {
    console.log('Loading...', (progress.loaded / progress.total) * 100, '%');
  },
  (error) => {
    console.error('Error loading VRM:', error);
  }
);
```

### VRMモデルの操作

```typescript
// 表情を変更
vrm.expressionManager?.setValue('happy', 1.0);

// ボーンを取得
const humanoid = vrm.humanoid;
const head = humanoid?.getNormalizedBoneNode('head');
if (head) {
  head.rotation.y = Math.PI / 4; // 45度回転
}

// ルックアット
vrm.lookAt?.target = new THREE.Vector3(0, 1.6, -2);
```

---

## Unity バックエンド: UniVRM統合

### UniVRMのインストール（必須）

1. `sdk/UniVRM-0.131.0_3b99.unitypackage` を Unity にインポート
2. Unity エディタで: `Assets > Import Package > Custom Package`
3. `UniVRM-0.131.0_3b99.unitypackage` を選択
4. すべてのファイルをインポート（**この手順を行わないと VRM はロードできません**）

### VRMモデルの配置

#### 方法1: エディタでインポート（開発時）

1. VRMファイルを Unity の `Assets/Models/` にドラッグ&ドロップ
2. UniVRMが自動的にVRMをインポート
3. Prefabが生成される
4. シーンに配置

#### 方法2: ランタイムロード（実行時）

`ArsistVRMLoader` を使用してランタイムでVRMをロードします。

```csharp
using Arsist.Runtime.VRM;

public class VRMLoaderExample : MonoBehaviour
{
    void Start()
    {
        var loader = gameObject.AddComponent<ArsistVRMLoader>();
        
        StartCoroutine(loader.LoadVRMFromStreamingAssets(
            "Models/avatar.vrm",
            OnVRMLoaded,
            OnVRMError
        ));
    }

    void OnVRMLoaded(GameObject vrmInstance)
    {
        Debug.Log("VRM loaded successfully");
        
        // スクリプト制御用に登録
        ArsistVRMLoader.SetupVRMForScripting(vrmInstance, "my_avatar");
        
        // シーンに配置
        vrmInstance.transform.position = new Vector3(0, 0, 2);
    }

    void OnVRMError(string error)
    {
        Debug.LogError($"VRM load failed: {error}");
    }
}
```

---

## スクリプトからVRMを制御

### Asset IDの設定

エディタでVRMオブジェクトに **Asset ID** を設定します。
例: `avatar`, `character_01`, `npc_shopkeeper`

### JavaScript APIの使用

```javascript
// 起動時に配置
scene.setPosition('avatar', 0, 0, 2);
scene.setRotation('avatar', 0, 180, 0);

// 表情を設定
vrm.setExpression('avatar', 'Joy', 100);

// ボーンを操作
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);

// 視線を向ける
vrm.lookAt('avatar', 0, 1.6, 0);
```

詳細は [3d-asset-api.md](3d-asset-api.md) を参照してください。

---

## VRMモデルの準備

### 推奨仕様

- **VRMバージョン**: VRM 0.x または VRM 1.0
- **ポリゴン数**: Quest向けは 10,000〜30,000 ポリゴン推奨
- **テクスチャサイズ**: 2048x2048 以下
- **マテリアル**: MToon シェーダー（UniVRMが自動変換）

### VRMモデルの作成

1. **VRoid Studio** を使用（最も簡単）
   - https://vroid.com/studio
   - キャラクターを作成してVRMエクスポート

2. **Blender + VRM Add-on**
   - 既存の3DモデルをVRMに変換
   - https://github.com/saturday06/VRM-Addon-for-Blender

3. **既存のVRMモデルを使用**
   - VRoid Hub: https://hub.vroid.com/
   - ニコニ立体: https://3d.nicovideo.jp/

### VRMモデルの最適化（Quest向け）

```csharp
// VRMインポート後、以下の最適化を推奨

// 1. テクスチャ圧縮
// Unity Inspector で Texture Import Settings:
// - Max Size: 2048
// - Compression: ASTC 6x6 (Android)
// - Generate Mip Maps: ON

// 2. メッシュ最適化
// VRM Prefab の Inspector:
// - Mesh Compression: Medium または High
// - Read/Write Enabled: OFF（ランタイム変更不要な場合）

// 3. BlendShape削減
// 使用しない表情は削除して軽量化
```

---

## ビルド設定

### StreamingAssetsへの配置

ランタイムロードする場合、VRMファイルを `StreamingAssets` に配置します。

```
UnityBackend/ArsistBuilder/Assets/StreamingAssets/
└── Models/
    ├── avatar.vrm
    ├── character_01.vrm
    └── npc_shopkeeper.vrm
```

### ビルドサイズの最適化

VRMモデルはファイルサイズが大きくなりがちです。以下の方法で最適化：

1. **テクスチャ圧縮**: PNG → ASTC (Android)
2. **ポリゴン削減**: Blenderで Decimate Modifier を使用
3. **不要なBlendShape削除**: 使わない表情を削除
4. **アニメーション削減**: 不要なアニメーションクリップを削除

---

## Quest / XREAL 対応

### Quest向け最適化

```json
// Adapters/MetaQuest/adapter.json
{
  "vrm": {
    "maxPolygons": 30000,
    "textureSize": 2048,
    "compression": "ASTC_6x6"
  }
}
```

### XREAL向け最適化

```json
// Adapters/XREAL_One/adapter.json
{
  "vrm": {
    "maxPolygons": 50000,
    "textureSize": 2048,
    "compression": "ETC2"
  }
}
```

---

## トラブルシューティング

### VRMが表示されない

1. **Asset IDが設定されているか確認**
   - Unity Hierarchy でオブジェクトを選択
   - Inspector で Asset ID フィールドを確認

2. **VRMが正しくインポートされているか確認**
   - Unity Console でエラーを確認
   - VRM Prefab が生成されているか確認

3. **スクリプトエラーを確認**
   - `scene.exists('avatar')` で存在確認
   - Unity Console で `[SceneWrapper]` のログを確認

### 表情が動かない

1. **BlendShape名を確認**
   - VRMモデルによって表情名が異なる
   - Unity で SkinnedMeshRenderer の BlendShapes を確認

2. **値の範囲を確認**
   - `vrm.setExpression()` は 0〜100 の範囲
   - 100 = 最大、0 = 無表情

### ボーンが動かない

1. **Humanoid設定を確認**
   - VRM Prefab の Inspector で Rig が Humanoid になっているか確認

2. **ボーン名を確認**
   - Unity の HumanBodyBones 列挙型に準拠した名前を使用
   - 例: `"RightUpperArm"`, `"Head"`, `"LeftHand"`

---

## サンプルプロジェクト

以下のサンプルを参照してください：

- [06_vrm_avatar_control.md](samples/06_vrm_avatar_control.md) — 基本的なVRM制御
- [07_vrm_interactive_avatar.md](samples/07_vrm_interactive_avatar.md) — インタラクティブなアバター

---

## 参考リンク

- **UniVRM公式**: https://github.com/vrm-c/UniVRM
- **@pixiv/three-vrm**: https://github.com/pixiv/three-vrm
- **VRM仕様**: https://vrm.dev/
- **VRoid Studio**: https://vroid.com/studio
- **VRoid Hub**: https://hub.vroid.com/
