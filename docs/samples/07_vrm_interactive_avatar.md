# サンプル 07: インタラクティブVRMアバター

このサンプルでは、ユーザーの操作に反応するインタラクティブなVRMアバターを作成します。

## 概要

- ボタンで挨拶ポーズ
- 音声入力（仮想）に反応して口を動かす
- 距離に応じて表情を変える
- スコアに応じてリアクション

---

## シーン構成

1. VRMモデル（Asset ID: `assistant`）
2. UIボタン3つ
3. テキスト表示（Binding ID: `status_text`）

---

## スクリプト

### スクリプト1: 初期化

**トリガー:** `onStart`

```javascript
// アバターを配置
scene.setPosition('assistant', 0, 0, 2.5);
scene.setRotation('assistant', 0, 180, 0);

// 初期表情
vrm.resetExpressions('assistant');
vrm.setExpression('assistant', 'Joy', 30);

// 初期状態を保存
store.set('avatar_state', 'idle');
store.set('score', 0);

ui.setText('status_text', 'アシスタント待機中');
log('アバター初期化完了');
```

---

### スクリプト2: 挨拶アクション

**トリガー:** `event` (イベント名: `greet`)

```javascript
// 状態を変更
store.set('avatar_state', 'greeting');

// 挨拶ポーズ
vrm.setBoneRotation('assistant', 'RightUpperArm', 0, 0, -90);
vrm.setBoneRotation('assistant', 'RightLowerArm', -45, 0, 0);

// 笑顔
vrm.resetExpressions('assistant');
vrm.setExpression('assistant', 'Joy', 100);

// ステータス更新
ui.setText('status_text', 'こんにちは！');

log('挨拶しました');
```

---

### スクリプト3: リセット

**トリガー:** `event` (イベント名: `reset_pose`)

```javascript
// ポーズをリセット
vrm.setBoneRotation('assistant', 'RightUpperArm', 0, 0, 0);
vrm.setBoneRotation('assistant', 'RightLowerArm', 0, 0, 0);
vrm.setBoneRotation('assistant', 'LeftUpperArm', 0, 0, 0);
vrm.setBoneRotation('assistant', 'LeftLowerArm', 0, 0, 0);

// 表情をリセット
vrm.resetExpressions('assistant');
vrm.setExpression('assistant', 'Joy', 30);

// 状態を戻す
store.set('avatar_state', 'idle');
ui.setText('status_text', 'アシスタント待機中');

log('ポーズをリセット');
```

---

### スクリプト4: スコア加算とリアクション

**トリガー:** `event` (イベント名: `add_score`)

```javascript
// スコアを加算
var score = store.get('score') || 0;
score += 10;
store.set('score', score);

// スコアに応じて表情を変える
if (score >= 50) {
  vrm.resetExpressions('assistant');
  vrm.setExpression('assistant', 'Joy', 100);
  ui.setText('status_text', 'すごい！スコア: ' + score);
  
  // 両手を上げて喜ぶ
  vrm.setBoneRotation('assistant', 'RightUpperArm', 0, 0, -120);
  vrm.setBoneRotation('assistant', 'LeftUpperArm', 0, 0, 120);
} else if (score >= 30) {
  vrm.resetExpressions('assistant');
  vrm.setExpression('assistant', 'Joy', 70);
  ui.setText('status_text', 'いいね！スコア: ' + score);
} else {
  vrm.resetExpressions('assistant');
  vrm.setExpression('assistant', 'Joy', 40);
  ui.setText('status_text', 'スコア: ' + score);
}

log('スコア加算: ' + score);
```

---

### スクリプト5: 口パク（リップシンク風）

**トリガー:** `interval` (100ms)

```javascript
// 話している状態かチェック
var state = store.get('avatar_state');
if (state === 'talking') {
  // ランダムに口の形を変える
  var vowels = ['A', 'I', 'U', 'E', 'O'];
  var randomVowel = vowels[Math.floor(Math.random() * vowels.length)];
  var randomValue = Math.random() * 60 + 20; // 20〜80
  
  vrm.setExpression('assistant', randomVowel, randomValue);
}
```

---

### スクリプト6: 話し始める

**トリガー:** `event` (イベント名: `start_talking`)

```javascript
store.set('avatar_state', 'talking');
ui.setText('status_text', '話しています...');
log('話し始めました');
```

---

### スクリプト7: 話し終わる

**トリガー:** `event` (イベント名: `stop_talking`)

```javascript
store.set('avatar_state', 'idle');

// 口を閉じる
vrm.setExpression('assistant', 'A', 0);
vrm.setExpression('assistant', 'I', 0);
vrm.setExpression('assistant', 'U', 0);
vrm.setExpression('assistant', 'E', 0);
vrm.setExpression('assistant', 'O', 0);

ui.setText('status_text', 'アシスタント待機中');
log('話し終わりました');
```

---

### スクリプト8: 距離に応じた反応

**トリガー:** `onUpdate`

```javascript
// アバターとの距離を計算（簡易版）
var z = scene.getPositionZ('assistant');

// 距離が近い場合
if (z < 1.5) {
  var currentExpr = store.get('proximity_reaction');
  if (currentExpr !== 'surprised') {
    vrm.resetExpressions('assistant');
    vrm.setExpression('assistant', 'Angry', 50); // 驚き（Surpriseがない場合）
    store.set('proximity_reaction', 'surprised');
  }
} else {
  var currentExpr = store.get('proximity_reaction');
  if (currentExpr === 'surprised') {
    vrm.resetExpressions('assistant');
    vrm.setExpression('assistant', 'Joy', 30);
    store.set('proximity_reaction', 'normal');
  }
}
```

---

## UIボタン設定

1. **挨拶ボタン** — イベント名: `greet`
2. **リセットボタン** — イベント名: `reset_pose`
3. **スコア+10ボタン** — イベント名: `add_score`
4. **話すボタン** — イベント名: `start_talking`
5. **停止ボタン** — イベント名: `stop_talking`

---

## 実行結果

- 挨拶ボタンで手を振る
- スコアボタンでスコアが増え、表情とポーズが変わる
- 話すボタンで口パクアニメーション開始
- アバターに近づくと驚いた表情になる

---

## 応用: 感情システム

```javascript
// 感情値を管理
var happiness = store.get('happiness') || 50;
var energy = store.get('energy') || 100;

// 時間経過でエネルギー減少
energy -= 0.1;
if (energy < 0) energy = 0;
store.set('energy', energy);

// エネルギーに応じて表情を変える
if (energy > 70) {
  vrm.setExpression('assistant', 'Joy', 80);
} else if (energy > 30) {
  vrm.setExpression('assistant', 'Joy', 40);
} else {
  vrm.setExpression('assistant', 'Sorrow', 60);
}

// ステータス表示
ui.setText('status_text', 'エネルギー: ' + Math.floor(energy) + '%');
```

---

## まとめ

このサンプルでは、VRMアバターを使った以下の機能を実装しました：

- ボタン操作による表情・ポーズ変更
- 状態管理（store）を使った複雑な挙動
- 口パクアニメーション
- 距離に応じた反応
- スコアシステムとの連携

これらを組み合わせることで、インタラクティブなARアシスタントやゲームキャラクターを作成できます。
