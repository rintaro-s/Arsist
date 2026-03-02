# サンプル 08: Pythonリモートコントロール

このサンプルでは、Python スクリプトからデバイス上の VRM アバターや 3D オブジェクトをリモート制御する方法を示します。

## 概要

- デバイス上で WebSocket サーバーを起動
- Python クライアントから WebSocket 経由で制御コマンドを送信
- VRM の表情・ポーズ・位置をリアルタイム制御
- センサーデータや AI の出力を VRM に反映

---

## 使用例

### ユースケース

1. **モーションキャプチャ**: PC のカメラでポーズを認識 → デバイスの VRM に反映
2. **AI アシスタント**: ChatGPT の感情分析 → VRM の表情に反映
3. **センサー連携**: IoT センサーの値 → 3D オブジェクトの位置・色に反映
4. **リモートデバッグ**: PC から VRM の動作をテスト

---

## セットアップ

### 1. Unity 側の設定

Arsist エディタの **Scene タブ → 右パネル（オブジェクト未選択時）→「Python リモートコントロール」** から有効にします。

> この項目は **VRM オブジェクトが 1 つ以上ある場合のみ**表示されます。

> **デフォルトは無効**です。有効化するとビルド成果物に WebSocket サーバーが組み込まれます。  
> 不要な場合は必ず無効のままにしてください（セキュリティ上の理由）。

| 項目 | 値 |
|---|---|
| リモートコントロールを有効にする | ✅ チェック |
| WebSocket ポート | `8765`（デフォルト） |
| 認証パスワード (任意) | 例: `myStrongPass123` |

> `認証パスワード` を設定した場合、Python 側の各コマンドに `authToken` を含める必要があります。

#### エンジン側スクリプト（任意）

Script タブで以下のスクリプトを追加すると、起動ログや UI ステータス表示を行えます。

| 項目 | 値 |
|---|---|
| トリガー | `onStart` |
| スクリプト名 | `RemoteControlInit` |

```javascript
// リモートコントロール初期化スクリプト
// ※ WebSocket サーバーはビルド時に自動組み込み済み（Project Settings で有効化が必要）

log('[RemoteControl] サーバーが起動しています');
log('[RemoteControl] デバイスの IP アドレスを確認して Python クライアントから接続してください');

// UI に接続待ち状態を表示（statusText 要素がある場合）
try {
  ui.setText('statusText', '待機中... (port: 8765)');
  ui.setColor('statusText', '#FFC300');
} catch (e) {
  // UI 要素が存在しない場合は無視
}
```

#### 手動でサーバーを起動する場合（エンジン設定を使わない）

`remote` API を使うと、スクリプトから任意のタイミングでサーバーを起動できます。

| 項目 | 値 |
|---|---|
| トリガー | `onStart` または `onClick` |
| スクリプト名 | `ManualRemoteServer` |

```javascript
// 手動で WebSocket サーバーを起動
// remote.startServer(port, password)
// password を空文字にすると認証なし

const port = 8765;
const password = 'myStrongPass123';

if (!remote.isRunning()) {
    remote.startServer(port, password);
    log(`[RemoteControl] Manual server started on ${port}`);
}

// 複数VRMを初期化（Asset IDを個別に設定しておく）
const vrmIds = ['avatar_main', 'avatar_sub'];
for (const id of vrmIds) {
    vrm.setExpression(id, 'Joy', 30);
}
```

### 2. VRM モデルの配置

1. VRM ファイルをインポート
2. シーンに配置
3. **Asset ID** を設定（例: `avatar_main`, `avatar_sub`）

> 複数VRMを制御する場合は、**Asset ID を必ずユニーク**にしてください。

### 3. ネットワーク設定

デバイスと PC を同じネットワークに接続します。

- **Quest**: Wi-Fi 経由で PC と同じネットワークに接続
- **XREAL**: Beam Pro または接続デバイスと PC を同じネットワークに接続

デバイスの IP アドレスを確認：
- Quest: 設定 > Wi-Fi > 接続中のネットワーク > IP アドレス
- XREAL: 接続デバイスの設定から確認

---

## Python クライアント

### 必要なライブラリ

```bash
pip install websocket-client
```

### 基本的な制御スクリプト

```python
#!/usr/bin/env python3
"""
Arsist VRM Remote Controller
デバイス上の VRM アバターをリモート制御
"""

import websocket
import json
import time

class ArsistRemoteController:
    def __init__(self, device_ip, port=8765, password=None):
        """
        Args:
            device_ip: デバイスの IP アドレス (例: "192.168.1.100")
            port: WebSocket ポート (デフォルト: 8765)
            password: 認証パスワード（未設定なら None）
        """
        self.url = f"ws://{device_ip}:{port}"
        self.ws = None
        self.password = password
        
    def connect(self):
        """デバイスに接続"""
        print(f"Connecting to {self.url}...")
        self.ws = websocket.create_connection(self.url)
        print("Connected!")
        
    def disconnect(self):
        """接続を切断"""
        if self.ws:
            self.ws.close()
            print("Disconnected")
    
    def send_command(self, cmd_type, method, **params):
        """
        コマンドを送信
        
        Args:
            cmd_type: "scene" または "vrm"
            method: メソッド名
            **params: パラメータ
        """
        command = {
            "type": cmd_type,
            "method": method,
            "parameters": params
        }
        if self.password:
            command["authToken"] = self.password
        self.ws.send(json.dumps(command))
    
    # === VRM 制御 ===
    
    def set_expression(self, avatar_id, expression, value):
        """
        表情を設定
        
        Args:
            avatar_id: VRM の Asset ID
            expression: 表情名 ("Joy", "Angry", "Sorrow" など)
            value: 0.0 ~ 100.0
        """
        self.send_command("vrm", "setExpression",
                         id=avatar_id,
                         expressionName=expression,
                         value=value)
    
    def set_bone_rotation(self, avatar_id, bone_name, pitch, yaw, roll):
        """
        ボーンの回転を設定
        
        Args:
            avatar_id: VRM の Asset ID
            bone_name: ボーン名 ("RightUpperArm" など)
            pitch, yaw, roll: 回転角度（度数法）
        """
        self.send_command("vrm", "setBoneRotation",
                         id=avatar_id,
                         boneName=bone_name,
                         pitch=pitch,
                         yaw=yaw,
                         roll=roll)
    
    def reset_expressions(self, avatar_id):
        """すべての表情をリセット"""
        self.send_command("vrm", "resetExpressions", id=avatar_id)
    
    def look_at(self, avatar_id, x, y, z):
        """視線を向ける"""
        self.send_command("vrm", "lookAt",
                         id=avatar_id,
                         x=x, y=y, z=z)
    
    # === 3D オブジェクト制御 ===
    
    def set_position(self, object_id, x, y, z):
        """オブジェクトの位置を設定"""
        self.send_command("scene", "setPosition",
                         id=object_id,
                         x=x, y=y, z=z)
    
    def move(self, object_id, dx, dy, dz):
        """オブジェクトを相対移動"""
        self.send_command("scene", "move",
                         id=object_id,
                         x=dx, y=dy, z=dz)
    
    def set_rotation(self, object_id, pitch, yaw, roll):
        """オブジェクトの回転を設定"""
        self.send_command("scene", "setRotation",
                         id=object_id,
                         pitch=pitch,
                         yaw=yaw,
                         roll=roll)
    
    def play_animation(self, object_id, anim_name):
        """アニメーションを再生"""
        self.send_command("scene", "playAnimation",
                         id=object_id,
                         animName=anim_name)


# === 使用例 ===

def main():
    # デバイスの IP アドレスを指定
    controller = ArsistRemoteController("192.168.1.100", port=8765, password="myStrongPass123")
    
    try:
        controller.connect()
        
        # 表情を変える
        print("Setting expression to Joy...")
        controller.set_expression("avatar", "Joy", 100)
        time.sleep(2)
        
        # 手を振るポーズ
        print("Waving hand...")
        controller.set_bone_rotation("avatar", "RightUpperArm", 0, 0, -90)
        controller.set_bone_rotation("avatar", "RightLowerArm", -30, 0, 0)
        time.sleep(2)
        
        # 表情をリセット
        print("Resetting expressions...")
        controller.reset_expressions("avatar")
        
        # ボーンをリセット
        controller.set_bone_rotation("avatar", "RightUpperArm", 0, 0, 0)
        controller.set_bone_rotation("avatar", "RightLowerArm", 0, 0, 0)
        
    finally:
        controller.disconnect()


if __name__ == "__main__":
    main()
```

---

## 応用例

### 例1: 感情に応じた表情変化

```python
def set_emotion(controller, avatar_id, emotion):
    """
    感情に応じて表情とポーズを設定
    
    Args:
        emotion: "happy", "sad", "angry", "neutral"
    """
    controller.reset_expressions(avatar_id)
    
    if emotion == "happy":
        controller.set_expression(avatar_id, "Joy", 100)
        # 両手を上げる
        controller.set_bone_rotation(avatar_id, "RightUpperArm", 0, 0, -120)
        controller.set_bone_rotation(avatar_id, "LeftUpperArm", 0, 0, 120)
        
    elif emotion == "sad":
        controller.set_expression(avatar_id, "Sorrow", 100)
        # 下を向く
        controller.set_bone_rotation(avatar_id, "Head", 20, 0, 0)
        
    elif emotion == "angry":
        controller.set_expression(avatar_id, "Angry", 100)
        # 腕を組む
        controller.set_bone_rotation(avatar_id, "RightUpperArm", 0, -45, -90)
        controller.set_bone_rotation(avatar_id, "LeftUpperArm", 0, 45, 90)
        
    else:  # neutral
        controller.set_expression(avatar_id, "Joy", 30)

# 使用例
controller = ArsistRemoteController("192.168.1.100")
controller.connect()
set_emotion(controller, "avatar", "happy")
```

---

### 例2: リアルタイム口パク

```python
import numpy as np

def lip_sync(controller, avatar_id, audio_level):
    """
    音声レベルに応じて口パク
    
    Args:
        audio_level: 0.0 ~ 1.0 の音声レベル
    """
    # 音声レベルに応じて口の開き具合を変える
    mouth_open = audio_level * 80  # 0 ~ 80
    
    # ランダムに母音を選択（よりリアルな口パク）
    vowels = ["A", "I", "U", "E", "O"]
    vowel = np.random.choice(vowels)
    
    controller.set_expression(avatar_id, vowel, mouth_open)

# 使用例（音声入力と連携）
import pyaudio
import struct

def audio_callback(in_data, frame_count, time_info, status):
    # 音声レベルを計算
    audio_data = struct.unpack(str(frame_count) + 'h', in_data)
    level = np.abs(np.array(audio_data)).mean() / 32768.0
    
    # 口パク
    lip_sync(controller, "avatar", level)
    
    return (in_data, pyaudio.paContinue)
```

---

### 例3: センサーデータの可視化

```python
def visualize_sensor_data(controller, temperature, humidity):
    """
    温度・湿度センサーのデータを 3D オブジェクトで可視化
    
    Args:
        temperature: 温度 (℃)
        humidity: 湿度 (%)
    """
    # 温度に応じてオブジェクトの高さを変える
    temp_height = temperature / 10.0  # 0℃ = 0m, 30℃ = 3m
    controller.set_position("temp_bar", 0, temp_height / 2, 0)
    controller.set_scale("temp_bar", 0.5, temp_height, 0.5)
    
    # 湿度に応じてオブジェクトの色を変える（スクリプト経由）
    humidity_color = f"#{int(humidity * 2.55):02x}00ff"
    # 注: 色変更は Unity スクリプト側で実装が必要
```

---

### 例4: AI チャットボットとの連携

```python
import openai

def chatbot_with_avatar(controller, avatar_id, user_input):
    """
    ChatGPT と連携して、感情に応じた表情を表示
    """
    # ChatGPT に質問
    response = openai.ChatCompletion.create(
        model="gpt-4",
        messages=[
            {"role": "system", "content": "あなたは感情豊かなアシスタントです。回答の最後に感情を [happy/sad/angry/neutral] で示してください。"},
            {"role": "user", "content": user_input}
        ]
    )
    
    answer = response.choices[0].message.content
    
    # 感情を抽出
    if "[happy]" in answer:
        set_emotion(controller, avatar_id, "happy")
    elif "[sad]" in answer:
        set_emotion(controller, avatar_id, "sad")
    elif "[angry]" in answer:
        set_emotion(controller, avatar_id, "angry")
    else:
        set_emotion(controller, avatar_id, "neutral")
    
    return answer.replace("[happy]", "").replace("[sad]", "").replace("[angry]", "").replace("[neutral]", "")
```

---

## トラブルシューティング

### 接続できない

1. **デバイスと PC が同じネットワークにいるか確認**
   - Quest: Wi-Fi 設定を確認
   - XREAL: 接続デバイスのネットワークを確認

2. **IP アドレスが正しいか確認**
   ```python
   # 接続テスト
   import socket
   sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
   result = sock.connect_ex(("192.168.1.100", 8765))
   if result == 0:
       print("Port is open")
   else:
       print("Port is closed")
   ```

3. **ファイアウォールを確認**
   - デバイス側でポート 8765 が開いているか確認

### コマンドが反映されない

1. **Asset ID が正しいか確認**
   ```python
   # 存在確認（Unity ログを確認）
   controller.send_command("scene", "setPosition", id="avatar", x=0, y=0, z=2)
   ```

2. **Unity ログを確認**
   - Android Logcat で `[ArsistWebSocket]` のログを確認

---

## まとめ

Python からデバイス上の VRM や 3D オブジェクトをリモート制御することで、以下が可能になります：

- **AI との連携**: ChatGPT の感情分析を VRM に反映
- **センサー連携**: IoT データの可視化
- **モーションキャプチャ**: PC のカメラでポーズ認識
- **リモートデバッグ**: PC から動作テスト

WebSocket プロトコルを使用しているため、Python 以外の言語（JavaScript、C#、Java など）からも制御可能です。
