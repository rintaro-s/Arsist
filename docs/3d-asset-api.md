# Arsist 3Dアセット制御 API リファレンス

Arsist スクリプトから3Dオブジェクト（GLB、VRM、Canvas等）を動的に操作するためのAPIです。

---

## scene — 3Dオブジェクト制御

`scene` オブジェクトは、シーン内の3Dオブジェクトを **ID ベース** で操作します。
エディタで配置したオブジェクトに **Asset ID** を設定することで、スクリプトから参照できます。

### Transform 操作

#### `scene.setPosition(id, x, y, z)`

オブジェクトの位置を設定します（ワールド座標系）。

```typescript
scene.setPosition(id: string, x: number, y: number, z: number): void
```

**例:**
```javascript
// 敵キャラクターを座標 (1, 0, 2) に配置
scene.setPosition('enemy_01', 1, 0, 2);

// メニューを目の前に配置
scene.setPosition('main_menu', 0, 1.5, 2.0);
```

---

#### `scene.move(id, deltaX, deltaY, deltaZ)`

オブジェクトを現在位置から相対的に移動します。

```typescript
scene.move(id: string, deltaX: number, deltaY: number, deltaZ: number): void
```

**例:**
```javascript
// プレイヤーを前方に0.1m移動（毎フレーム呼ぶと前進）
scene.move('player_model', 0, 0, 0.1);

// オブジェクトを上に浮かせる
scene.move('floating_item', 0, 0.01, 0);
```

---

#### `scene.setRotation(id, pitch, yaw, roll)`

オブジェクトの回転を設定します（オイラー角、度数法）。

```typescript
scene.setRotation(id: string, pitch: number, yaw: number, roll: number): void
```

**パラメータ:**
- `pitch` — X軸回転（上下）
- `yaw` — Y軸回転（左右）
- `roll` — Z軸回転（傾き）

**例:**
```javascript
// オブジェクトをY軸で90度回転
scene.setRotation('door', 0, 90, 0);

// 完全にリセット
scene.setRotation('cube', 0, 0, 0);
```

---

#### `scene.rotate(id, deltaPitch, deltaYaw, deltaRoll)`

オブジェクトを現在の回転から相対的に回転します。

```typescript
scene.rotate(id: string, deltaPitch: number, deltaYaw: number, deltaRoll: number): void
```

**例:**
```javascript
// 毎フレーム呼んでY軸で回転させる
scene.rotate('spinning_coin', 0, 1, 0);
```

---

#### `scene.setScale(id, x, y, z)`

オブジェクトのスケールを設定します。

```typescript
scene.setScale(id: string, x: number, y: number, z: number): void
```

**例:**
```javascript
// オブジェクトを2倍に拡大
scene.setScale('target', 2, 2, 2);

// X方向のみ引き伸ばす
scene.setScale('wall', 5, 1, 1);
```

---

#### `scene.setUniformScale(id, scale)`

オブジェクトを均等にスケールします。

```typescript
scene.setUniformScale(id: string, scale: number): void
```

**例:**
```javascript
// オブジェクトを1.5倍に拡大
scene.setUniformScale('powerup', 1.5);
```

---

### アニメーション制御

#### `scene.playAnimation(id, animName)`

GLBモデルに含まれるアニメーションを再生します。

```typescript
scene.playAnimation(id: string, animName: string): void
```

**例:**
```javascript
// 歩行アニメーションを再生
scene.playAnimation('character', 'Walk');

// 攻撃アニメーション
scene.playAnimation('enemy', 'Attack');
```

---

#### `scene.stopAnimation(id)`

アニメーションを停止します。

```typescript
scene.stopAnimation(id: string): void
```

**例:**
```javascript
scene.stopAnimation('character');
```

---

#### `scene.setAnimationSpeed(id, speed)`

アニメーションの再生速度を変更します。

```typescript
scene.setAnimationSpeed(id: string, speed: number): void
```

**パラメータ:**
- `speed` — 再生速度（1.0 = 通常、2.0 = 2倍速、0.5 = 半速）

**例:**
```javascript
// 2倍速で再生
scene.setAnimationSpeed('runner', 2.0);

// スローモーション
scene.setAnimationSpeed('explosion', 0.3);
```

---

### 表示制御

#### `scene.setVisible(id, visible)`

オブジェクトの表示/非表示を切り替えます。

```typescript
scene.setVisible(id: string, visible: boolean): void
```

**例:**
```javascript
// オブジェクトを非表示
scene.setVisible('secret_door', false);

// 表示
scene.setVisible('reward', true);
```

---

### 位置取得

#### `scene.getPositionX(id)` / `getPositionY(id)` / `getPositionZ(id)`

オブジェクトの現在位置を取得します。

```typescript
scene.getPositionX(id: string): number
scene.getPositionY(id: string): number
scene.getPositionZ(id: string): number
```

**例:**
```javascript
var x = scene.getPositionX('player');
var y = scene.getPositionY('player');
var z = scene.getPositionZ('player');
log('Player position: ' + x + ', ' + y + ', ' + z);
```

---

### ユーティリティ

#### `scene.exists(id)`

指定したIDのオブジェクトが存在するかチェックします。

```typescript
scene.exists(id: string): boolean
```

**例:**
```javascript
if (scene.exists('enemy_01')) {
  scene.move('enemy_01', 0, 0, 0.1);
}
```

---

## vrm — VRMモデル専用制御

`vrm` オブジェクトは、VRMモデル専用の高度な制御を提供します。

### ボーン制御

#### `vrm.setBoneRotation(id, boneName, pitch, yaw, roll)`

VRMモデルのHumanoidボーンの回転を設定します。

```typescript
vrm.setBoneRotation(id: string, boneName: string, pitch: number, yaw: number, roll: number): void
```

**パラメータ:**
- `boneName` — Unityの `HumanBodyBones` 列挙型に準拠したボーン名
  - 例: `"Head"`, `"RightUpperArm"`, `"LeftHand"`, `"Spine"` など

**例:**
```javascript
// 右腕を上げる
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);

// 頭を傾ける
vrm.setBoneRotation('avatar', 'Head', 15, 0, 0);

// 左手を前に出す
vrm.setBoneRotation('avatar', 'LeftLowerArm', -45, 0, 0);
```

**主要なボーン名:**
- `Head`, `Neck`
- `Spine`, `Chest`, `UpperChest`
- `RightShoulder`, `RightUpperArm`, `RightLowerArm`, `RightHand`
- `LeftShoulder`, `LeftUpperArm`, `LeftLowerArm`, `LeftHand`
- `RightUpperLeg`, `RightLowerLeg`, `RightFoot`
- `LeftUpperLeg`, `LeftLowerLeg`, `LeftFoot`

---

#### `vrm.rotateBone(id, boneName, deltaPitch, deltaYaw, deltaRoll)`

ボーンを現在の回転から相対的に回転します。

```typescript
vrm.rotateBone(id: string, boneName: string, deltaPitch: number, deltaYaw: number, deltaRoll: number): void
```

**例:**
```javascript
// 頭を少しずつ回す（毎フレーム呼ぶ）
vrm.rotateBone('avatar', 'Head', 0, 0.5, 0);
```

---

### 表情制御

#### `vrm.setExpression(id, expressionName, value)`

VRMモデルの表情（BlendShape）を設定します。

```typescript
vrm.setExpression(id: string, expressionName: string, value: number): void
```

**パラメータ:**
- `expressionName` — BlendShape名（モデルに依存）
  - VRM標準: `"Joy"`, `"Angry"`, `"Sorrow"`, `"Fun"`, `"Blink"`, `"A"`, `"I"`, `"U"`, `"E"`, `"O"` など
- `value` — 0.0（無表情）〜 100.0（最大）

**例:**
```javascript
// 笑顔
vrm.setExpression('avatar', 'Joy', 100);

// 悲しい表情
vrm.setExpression('avatar', 'Sorrow', 80);

// まばたき
vrm.setExpression('avatar', 'Blink', 100);

// 口の形（あ）
vrm.setExpression('avatar', 'A', 50);
```

---

#### `vrm.resetExpressions(id)`

すべての表情をリセット（0に戻す）します。

```typescript
vrm.resetExpressions(id: string): void
```

**例:**
```javascript
vrm.resetExpressions('avatar');
```

---

### アニメーション制御

#### `vrm.playAnimation(id, animName)`

VRMモデルのアニメーションを再生します。

```typescript
vrm.playAnimation(id: string, animName: string): void
```

**例:**
```javascript
vrm.playAnimation('avatar', 'Wave');
```

---

#### `vrm.setAnimationSpeed(id, speed)`

アニメーション速度を設定します。

```typescript
vrm.setAnimationSpeed(id: string, speed: number): void
```

---

### ルックアット制御

#### `vrm.lookAt(id, x, y, z)`

VRMモデルの視線を特定の座標に向けます。

```typescript
vrm.lookAt(id: string, x: number, y: number, z: number): void
```

**例:**
```javascript
// カメラの方を向かせる
vrm.lookAt('avatar', 0, 1.6, 0);

// 特定のオブジェクトを見る
var targetX = scene.getPositionX('target');
var targetY = scene.getPositionY('target');
var targetZ = scene.getPositionZ('target');
vrm.lookAt('avatar', targetX, targetY, targetZ);
```

---

### ユーティリティ

#### `vrm.exists(id)`

指定したIDのVRMが存在するかチェックします。

```typescript
vrm.exists(id: string): boolean
```

---

## 使用例

### 例1: キャラクターを歩かせる

```javascript
// onUpdate トリガーで毎フレーム実行
scene.move('character', 0, 0, 0.01); // 前進
scene.playAnimation('character', 'Walk');
```

---

### 例2: VRMアバターに表情をつける

```javascript
// ボタンクリック時に笑顔
vrm.resetExpressions('avatar');
vrm.setExpression('avatar', 'Joy', 100);
vrm.setExpression('avatar', 'Blink', 0);
```

---

### 例3: オブジェクトを回転させる

```javascript
// interval: 16ms (60FPS相当) で回転
scene.rotate('coin', 0, 2, 0);
```

---

### 例4: VRMの手を振る動作

```javascript
// 右腕を上げて手を振る
vrm.setBoneRotation('avatar', 'RightUpperArm', 0, 0, -90);
vrm.setBoneRotation('avatar', 'RightLowerArm', -45, 0, 0);

// 手を振るアニメーション（interval で繰り返し）
var angle = Math.sin(Date.now() / 200) * 30;
vrm.rotateBone('avatar', 'RightHand', 0, angle, 0);
```

---

## 注意事項

### ボーン操作とアニメーションの競合

`vrm.setBoneRotation()` でボーンを直接操作する場合、Animatorによるアニメーション再生と競合する可能性があります。
Unity側で `LateUpdate` のタイミングで適用するなどの工夫が必要になる場合があります。

### VRM表情の互換性

表情（BlendShape）の名前はVRMモデルによって異なる場合があります。
VRM 0.x と VRM 1.0 では仕様が異なるため、使用するモデルの仕様を確認してください。

---

## Quest / XREAL 対応

すべてのAPIは **IL2CPP環境** で動作します。
- Meta Quest
- XREAL One

両デバイスで完全に動作確認済みです。
