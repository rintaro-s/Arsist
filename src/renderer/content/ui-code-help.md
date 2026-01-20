# UIコード（HTML/CSS/JS）ガイド

## まず結論：`<!DOCTYPE html>` は必要？

- **あなたが入力するのは「HTML断片（bodyの中身）」です**。つまり `<html>`, `<head>`, `<body>` を自分で書く必要はありません。
- **プレビュー/ビルド時にArsistが自動でHTMLドキュメントとしてラップします**（その際に `<!DOCTYPE html>` も入れます）。
- 逆に、断片に `<!DOCTYPE html>` や `<html>` を書いてしまうと、ネストして挙動が壊れやすいので **書かない方が安全**です。

## できないこと（制約）

プレビューは安全のために iframe sandbox で動いています。Unity実機（Android WebView）でも近い制約があります。

- OSのファイルに直接アクセスできない（JSからローカルファイルを読む等は不可）
- ネイティブ機能（センサー等）は **`window.ArsistBridge` 経由**でやり取りする
- 外部サイトへ勝手に通信するのは推奨しない（必要ならアプリ仕様として明示する）

## プレビューでのエラー表示

- JSエラーや Promiseの未処理エラーが出たら、プレビュー上に **「プレビューできません」** とエラー内容が表示されます。
- 同じ内容は下の **「コンソール」** タブにも出ます。

## `window.ArsistBridge`（UI ↔ エンジン橋渡し）

プレビューでは「ダミーのArsistBridge」を用意しています。Unity実機ではネイティブ側が注入する想定です。

### 送信（UI → エンジン）

```js
window.ArsistBridge.sendEvent('ui.action', {
  id: 'btnStart',
  value: 1,
})
```

- `name`: `string`（イベント名）
- `data`: JSONとしてシリアライズ可能な値（object/array/string/number/bool/null）

### 取得/設定（プレビュー用の簡易ストア）

```js
window.ArsistBridge.setData('score', 10)
const score = window.ArsistBridge.getData('score')
```

※ 実機側は実装により返り値が `Promise` になる可能性があります（この点は今後アダプターで統一します）。

### 受信（エンジン → UI）

```js
window.onArsistData = function (data) {
  // data: object
  console.log('from engine', data)
}
```

プレビューでは手動で試すために：

```js
window.ArsistBridge.__simulateIncoming({ hello: 'world' })
```

## アセット（GLB/GLTF）について

- **Assetsに置いただけではシーンに出ません**。
- 下の「アセット」タブで `.glb/.gltf` を選び、**「シーンに追加」**を押すと、現在のシーンに `type: model` のオブジェクトが追加されます。
- ビルド時にプロジェクトの `Assets/` は Unity 側の `Assets/ArsistProjectAssets/` にコピーされ、モデルはそこから参照されます。

## 最小サンプル（動くHUD）

### HTML（断片）

```html
<div class="hud">
  <div class="row">
    <div class="title">Hello AR</div>
    <button onclick="onTap()">Tap</button>
  </div>
</div>
```

### CSS

```css
.hud { color: white; padding: 16px; background: rgba(0,0,0,.35); }
.row { display:flex; gap: 12px; align-items:center; }
.title { font-weight: 700; }
button { padding: 8px 12px; }
```

### JS

```js
function onTap() {
  window.ArsistBridge?.sendEvent('ui.tap', { t: Date.now() })
}
```
