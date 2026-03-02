# サンプル 05: 3Dオブジェクト制御

このサンプルでは、`scene` APIを使って3Dオブジェクト（GLBモデル）を動的に制御する方法を示します。

## 概要

- GLBモデルを配置し、Asset IDを設定
- スクリプトから位置・回転・スケールを操作
- アニメーションを再生

---

## 手順

### 1. シーンにGLBモデルを配置

1. エディタの「シーン」タブを開く
2. 3Dモデル（GLB）をインポート
3. シーンに配置
4. プロパティパネルで **Asset ID** を `robot` に設定

### 2. スクリプトを作成

「スクリプト」タブを開き、新しいスクリプトを作成します。

#### スクリプト1: 起動時にロボットを配置

**トリガー:** `onStart`

```javascript
// ロボットを目の前に配置
scene.setPosition('robot', 0, 0, 2);
scene.setScale('robot', 1.5, 1.5, 1.5);

log('ロボットを配置しました');
```

#### スクリプト2: ロボットを回転させる

**トリガー:** `onUpdate`

```javascript
// 毎フレーム、Y軸で1度ずつ回転
scene.rotate('robot', 0, 1, 0);
```

#### スクリプト3: ボタンでアニメーション再生

**トリガー:** `event` (イベント名: `play_animation`)

```javascript
// ロボットの歩行アニメーションを再生
scene.playAnimation('robot', 'Walk');
log('アニメーション再生');
```

### 3. UIボタンを追加

1. 「UI」タブを開く
2. ボタンを追加
3. ボタンのイベント名を `play_animation` に設定

---

## 実行結果

- アプリ起動時、ロボットが目の前に表示される
- ロボットが自動的に回転し続ける
- ボタンを押すと歩行アニメーションが再生される

---

## 応用例

### 複数のオブジェクトを制御

```javascript
// 複数のオブジェクトを配置
scene.setPosition('robot_01', -1, 0, 2);
scene.setPosition('robot_02', 1, 0, 2);

// それぞれ異なる速度で回転
scene.rotate('robot_01', 0, 1, 0);
scene.rotate('robot_02', 0, -2, 0);
```

### オブジェクトを前後に移動

```javascript
// 往復運動
var time = Date.now() / 1000;
var z = Math.sin(time) * 2 + 3; // 1m〜5mの間を往復
scene.setPosition('robot', 0, 0, z);
```

### 条件付きアニメーション

```javascript
// ロボットが近くにいる時だけアニメーション再生
var z = scene.getPositionZ('robot');
if (z < 3) {
  scene.playAnimation('robot', 'Wave');
} else {
  scene.stopAnimation('robot');
}
```
