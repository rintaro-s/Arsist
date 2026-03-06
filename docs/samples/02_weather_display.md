# サンプル 02: リアルタイム天気表示

OpenWeatherMap API から気温を取得し、10 秒ごとに UI を更新します。

## 設定

| 項目 | 値 |
|---|---|
| トリガー | `interval` |
| 間隔 | `10000` (10 秒) |
| スクリプト名 | `WeatherDisplay` |

## 事前準備

1. [OpenWeatherMap](https://openweathermap.org/api) で無料 API キーを取得
2. UI エディタで以下の要素を作成し Binding ID を設定:

| Binding ID | 要素タイプ | 用途 |
|---|---|---|
| `cityName` | Text | 都市名 |
| `temperature` | Text | 気温 |
| `weatherDesc` | Text | 天気の説明 |
| `weatherPanel` | Panel | 背景パネル |

## コード

```javascript
var API_KEY = 'ほげほげ';
var CITY = 'Tokyo';

var url = 'https://api.openweathermap.org/data/2.5/weather?q=' + CITY + '&appid=' + API_KEY + '&units=metric&lang=ja';

api.get(url, function(res) {
  if (res === null) {
    ui.setText('weatherDesc', '取得失敗');
    return;
  }
  var data = JSON.parse(res);

  var tempC = Math.round(data.main.temp);
  var desc = data.weather[0].description;
  var city = data.name;

  ui.setText('cityName', city);
  ui.setText('temperature', tempC + '°C');
  ui.setText('weatherDesc', desc);

  log('天気更新: ' + city + ' ' + tempC + '°C ' + desc);
});
```
