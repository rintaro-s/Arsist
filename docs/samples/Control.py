"""
Arsist Remote Controller
========================
WebSocket 経由で Arsist アプリ上の VRM・3D オブジェクトを制御するサンプル。

## クイックスタート

  # 接続 → 能力検出 → 動作確認デモを全自動実行
  python Control.py --demo

  # 登録済みオブジェクト ID 一覧を表示
  python Control.py --list-ids

  # 指定 VRM の能力に合わせた Python サンプルを標準出力へ生成
  python Control.py --generate-sample --avatar-id avatar

  # カスタム操作 (デモ実行)
  python Control.py --host 127.0.0.1 --port 8765 --password 0000 --avatar-id avatar --demo

## ADB ポートフォワード (Quest 使用時)
  adb -s <serial> forward tcp:8765 tcp:8765

## 必要パッケージ
  pip install websocket-client
"""

import argparse
import json
import os
import sys
import textwrap
import time
import uuid

try:
    import websocket
except ImportError:
    sys.exit("[ERROR] websocket-client が必要です: pip install websocket-client")


# ======================================================
# メインコントローラクラス
# ======================================================

class ArsistRemoteController:
    """Arsist WebSocket サーバーへの接続・コマンド送受信を担うクラス"""

    def __init__(self, device_ip: str, port: int = 8765, password: str = None):
        """
        Args:
            device_ip: 接続先 IP。ADB port forward なら "127.0.0.1"
            port:      WebSocket ポート (デフォルト 8765)
            password:  認証パスワード。None なら認証なし
        """
        self.device_ip = device_ip
        self.port = port
        self.password = password
        self.ws: "websocket.WebSocket | None" = None
        self.url: "str | None" = None

    # --------------------------------------------------
    # 接続管理
    # --------------------------------------------------

    def _candidate_hosts(self):
        seen, result = set(), []
        for h in [self.device_ip, "127.0.0.1", "localhost"]:
            if h and h not in seen:
                seen.add(h)
                result.append(h)
        return result

    def connect(self, timeout: float = 5) -> None:
        """デバイスに接続する。失敗時は ConnectionError を送出"""
        errors = []
        for host in self._candidate_hosts():
            url = f"ws://{host}:{self.port}"
            print(f"  Trying {url} ...")
            try:
                ws = websocket.create_connection(url, timeout=timeout)
                self.ws = ws
                self.url = url
                print(f"  Connected: {url}")
                return
            except Exception as err:
                errors.append(f"  - {url} => {err}")

        raise ConnectionError(
            "WebSocket 接続に失敗しました。\n"
            "確認事項:\n"
            "  1) Quest 側でアプリが起動している\n"
            "  2) ADB port forward が完了している\n"
            "     例: adb -s <serial> forward tcp:8765 tcp:8765\n"
            "試行ログ:\n" + "\n".join(errors)
        )

    def disconnect(self) -> None:
        """接続を切断する"""
        if self.ws:
            try:
                self.ws.close()
            except Exception:
                pass
            self.ws = None
            print("  Disconnected.")

    # --------------------------------------------------
    # fire-and-forget コマンド送信
    # --------------------------------------------------

    def send_command(self, cmd_type: str, method: str, **params) -> None:
        """
        レスポンス不要のコマンドを送信する

        Args:
            cmd_type: "scene" / "vrm" / "script"
            method:   メソッド名
            **params: パラメータ (id, x, y, z, etc.)
        """
        if not self.ws:
            raise RuntimeError("未接続です。先に connect() を呼んでください。")
        command = {"type": cmd_type, "method": method, "parameters": params}
        if self.password:
            command["authToken"] = self.password
        self.ws.send(json.dumps(command))

    # --------------------------------------------------
    # 双方向クエリ (レスポンスあり)
    # --------------------------------------------------

    def query(self, cmd_type: str, method: str, timeout: float = 6.0, **params) -> dict:
        """
        レスポンスが必要なクエリを送信し、結果 dict を返す

        Args:
            cmd_type: 通常は "query"
            method:   "getInfo" / "getIds" / "getState" / "ping" etc.
            timeout:  応答待ち最大秒数

        Returns:
            {"requestId": str, "success": bool, "data": {...}}
        """
        if not self.ws:
            raise RuntimeError("未接続です。先に connect() を呼んでください。")

        req_id = uuid.uuid4().hex[:8]
        command = {
            "type": cmd_type,
            "method": method,
            "requestId": req_id,
            "parameters": params,
        }
        if self.password:
            command["authToken"] = self.password
        self.ws.send(json.dumps(command))

        # 正しい requestId が返るまで受信し続ける
        deadline = time.time() + timeout
        while time.time() < deadline:
            remaining = deadline - time.time()
            if remaining <= 0:
                break
            try:
                self.ws.settimeout(min(remaining, 1.5))
                raw = self.ws.recv()
                resp = json.loads(raw)
                if resp.get("requestId") == req_id:
                    return resp
            except websocket.WebSocketTimeoutException:
                continue
            except Exception as e:
                raise RuntimeError(f"クエリ受信エラー: {e}")

        raise TimeoutError(
            f"requestId={req_id} に対するレスポンスが {timeout}s 以内に届きません。\n"
            "ヒント: 新しいビルドの APK をインストールしてください。"
        )

    # --------------------------------------------------
    # scene コマンド (3D オブジェクト操作)
    # --------------------------------------------------

    def set_position(self, object_id: str, x: float, y: float, z: float) -> None:
        """ワールド絶対座標で配置"""
        self.send_command("scene", "setPosition", id=object_id, x=x, y=y, z=z)

    def move(self, object_id: str, dx: float, dy: float, dz: float) -> None:
        """現在位置から相対移動"""
        self.send_command("scene", "move", id=object_id, x=dx, y=dy, z=dz)

    def set_rotation(self, object_id: str, pitch: float, yaw: float, roll: float) -> None:
        """回転を絶対指定 (オイラー角。pitch=X, yaw=Y, roll=Z)"""
        self.send_command("scene", "setRotation", id=object_id,
                          pitch=pitch, yaw=yaw, roll=roll)

    def rotate(self, object_id: str, dp: float, dy_: float, dr: float) -> None:
        """現在の回転から相対回転"""
        self.send_command("scene", "rotate", id=object_id,
                          pitch=dp, yaw=dy_, roll=dr)

    def set_scale(self, object_id: str, x: float, y: float, z: float) -> None:
        """スケールを個別指定"""
        self.send_command("scene", "setScale", id=object_id, x=x, y=y, z=z)

    def set_uniform_scale(self, object_id: str, scale: float) -> None:
        """均等スケールを指定"""
        self.send_command("scene", "setUniformScale", id=object_id, scale=scale)

    def set_visible(self, object_id: str, visible: bool) -> None:
        """表示 / 非表示を切り替える"""
        self.send_command("scene", "setVisible", id=object_id, visible=visible)

    def play_animation(self, object_id: str, anim_name: str) -> None:
        """アニメーションを再生 (scene)"""
        self.send_command("scene", "playAnimation", id=object_id, animName=anim_name)

    def stop_animation(self, object_id: str) -> None:
        """アニメーションを停止"""
        self.send_command("scene", "stopAnimation", id=object_id)

    def set_animation_speed(self, object_id: str, speed: float) -> None:
        """アニメーション再生速度を変更 (1.0 = 通常)"""
        self.send_command("scene", "setAnimationSpeed", id=object_id, speed=speed)

    # --------------------------------------------------
    # vrm コマンド (VRM 専用操作)
    # --------------------------------------------------

    def set_expression(self, avatar_id: str, expression: str, value: float) -> None:
        """
        表情 (BlendShape) を設定

        Args:
            avatar_id:  VRM の Asset ID
            expression: BlendShape 名 (例: "Joy", "A", "Fcl_ALL_Neutral" など)
            value:      0.0 ~ 100.0
        """
        self.send_command("vrm", "setExpression",
                          id=avatar_id, expressionName=expression, value=value)

    def reset_expressions(self, avatar_id: str) -> None:
        """すべての表情をリセット"""
        self.send_command("vrm", "resetExpressions", id=avatar_id)

    def set_bone_rotation(self, avatar_id: str, bone_name: str,
                          pitch: float, yaw: float, roll: float) -> None:
        """
        ボーン回転を絶対指定 (Humanoid rig が必要)

        Args:
            bone_name: Unity HumanBodyBones 名
                       例: "Head", "Neck", "Spine",
                           "RightUpperArm", "LeftUpperArm",
                           "RightLowerArm", "LeftLowerArm"
        """
        self.send_command("vrm", "setBoneRotation",
                          id=avatar_id, boneName=bone_name,
                          pitch=pitch, yaw=yaw, roll=roll)

    def rotate_bone(self, avatar_id: str, bone_name: str,
                    dp: float, dy_: float, dr: float) -> None:
        """ボーンを現在の向きから相対回転"""
        self.send_command("vrm", "rotateBone",
                          id=avatar_id, boneName=bone_name,
                          pitch=dp, yaw=dy_, roll=dr)

    def look_at(self, avatar_id: str, x: float, y: float, z: float) -> None:
        """VRM の視線を指定ワールド座標へ向ける"""
        self.send_command("vrm", "lookAt", id=avatar_id, x=x, y=y, z=z)

    def play_animation_vrm(self, avatar_id: str, anim_name: str) -> None:
        """VRM のアニメーションを再生"""
        self.send_command("vrm", "playAnimation", id=avatar_id, animName=anim_name)

    def set_animation_speed_vrm(self, avatar_id: str, speed: float) -> None:
        """VRM アニメーション速度を変更"""
        self.send_command("vrm", "setAnimationSpeed", id=avatar_id, speed=speed)

    # --------------------------------------------------
    # クエリ API (レスポンスあり — サーバー側対応ビルドが必要)
    # --------------------------------------------------

    def get_ids(self) -> dict:
        """
        登録済みの VRM ID と Scene オブジェクト ID の一覧を取得

        Returns:
            {"vrmIds": [str, ...], "sceneIds": [str, ...]}
        """
        resp = self.query("query", "getIds")
        if not resp.get("success"):
            raise RuntimeError(f"getIds 失敗: {resp.get('error')}")
        return resp["data"]

    def get_info(self, avatar_id: str) -> dict:
        """
        VRM の能力情報を取得する

        Returns:
            {
              "Id": str,
              "Expressions": [str, ...],    # 利用可能な BlendShape 名一覧
              "HumanoidBones": [str, ...],  # 利用可能な Humanoid ボーン名一覧
              "HasHumanoid": bool,
              "Position": [x, y, z],
              "Rotation": [pitch, yaw, roll],
              "Scale":    [x, y, z],
            }
        """
        resp = self.query("query", "getInfo", id=avatar_id)
        if not resp.get("success"):
            raise RuntimeError(f"getInfo 失敗: {resp.get('error')}")
        return resp["data"]

    def get_state(self, object_id: str) -> dict:
        """
        オブジェクトの現在 Transform 状態を取得

        Returns:
            {"Id": str, "Position": [x,y,z], "Rotation": [p,y,r], "Scale": [x,y,z]}
        """
        resp = self.query("query", "getState", id=object_id)
        if not resp.get("success"):
            raise RuntimeError(f"getState 失敗: {resp.get('error')}")
        return resp["data"]

    def ping(self) -> float:
        """サーバーへ ping を送信し、往復時間 (ms) を返す"""
        t0 = time.time()
        resp = self.query("query", "ping")
        return (time.time() - t0) * 1000.0

    # --------------------------------------------------
    # サンプルスクリプト自動生成
    # --------------------------------------------------

    def generate_sample_script(self, avatar_id: str, info: dict = None) -> str:
        """
        VRM の能力情報に基づいて、コピペで動く Python サンプルを生成する

        Args:
            avatar_id: 対象 VRM の Asset ID
            info:      get_info() の結果 (None の場合は自動取得)

        Returns:
            実行可能な Python コード文字列
        """
        if info is None:
            info = self.get_info(avatar_id)

        expressions  = info.get("Expressions", [])
        bones        = info.get("HumanoidBones", [])
        has_humanoid = info.get("HasHumanoid", False)
        pos          = info.get("Position", [0.0, 0.0, 1.5])

        # 利用したい表情候補 (一般 VRM に多い名前を優先)
        PREFERRED_EXPRS = [
            "Joy", "A", "aa", "Blink", "Neutral", "Angry", "Sorrow", "Fun",
            "Fcl_ALL_Neutral", "Fcl_ALL_Joy", "Fcl_ALL_Angry", "Fcl_MTH_A",
        ]
        selected_expr  = next((e for e in PREFERRED_EXPRS if e in expressions), None)
        if selected_expr is None and expressions:
            selected_expr = expressions[0]

        # 利用したいボーン候補
        PREFERRED_BONES = [
            "RightUpperArm", "LeftUpperArm", "RightLowerArm", "LeftLowerArm",
            "Head", "Neck", "Spine",
        ]
        selected_bone  = next((b for b in PREFERRED_BONES if b in bones), None)
        if selected_bone is None and bones:
            selected_bone = bones[0]

        pass_arg = f'"{self.password}"' if self.password else "None"
        right_arm = "RightUpperArm" if "RightUpperArm" in bones else None
        left_arm  = "LeftUpperArm"  if "LeftUpperArm"  in bones else None

        L = []
        add = L.append

        add('"""')
        add(f'Auto-generated Arsist sample for VRM  "{avatar_id}"')
        add(f'  Expressions : {len(expressions)} available')
        add(f'  Humanoid    : {"YES  (" + str(len(bones)) + " bones)" if has_humanoid else "NO"}')
        add(f'  Current pos : x={pos[0]:.2f}  y={pos[1]:.2f}  z={pos[2]:.2f}')
        add('"""')
        add("import time")
        add("from Control import ArsistRemoteController")
        add("")
        add(f'ctrl = ArsistRemoteController("{self.device_ip}", port={self.port}, password={pass_arg})')
        add("ctrl.connect()")
        add("time.sleep(2)  # ランタイム初期化待ち")
        add("")
        add("# ── 絶対座標・向きで配置 ──────────────────────────")
        add(f'ctrl.set_position("{avatar_id}", {pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})')
        add(f'ctrl.set_rotation("{avatar_id}", 0, 0, 0)   # 正面を向かせる (yaw=0)')
        add(f'ctrl.set_uniform_scale("{avatar_id}", 1.0)')
        add("time.sleep(0.5)")
        add("")

        # 表情ブロック
        if selected_expr:
            preview = ", ".join(expressions[:6]) + ("..." if len(expressions) > 6 else "")
            add(f"# ── 表情制御  ({len(expressions)} 種: {preview}) ──────")
            add(f'ctrl.set_expression("{avatar_id}", "{selected_expr}", 80)')
            add("time.sleep(1.0)")
            add(f'ctrl.reset_expressions("{avatar_id}")')
            add("time.sleep(0.3)")
            if len(expressions) > 1:
                second = next((e for e in expressions if e != selected_expr), None)
                if second:
                    add(f'ctrl.set_expression("{avatar_id}", "{second}", 60)')
                    add("time.sleep(0.8)")
                    add(f'ctrl.reset_expressions("{avatar_id}")')
                    add("time.sleep(0.3)")
            add("")
        else:
            add("# このVRMには利用可能なBlendShape表情がありません")
            add("")

        # ボーンブロック
        if has_humanoid and selected_bone:
            bone_preview = ", ".join(bones[:6]) + ("..." if len(bones) > 6 else "")
            add(f"# ── ボーン制御  ({len(bones)} 種: {bone_preview}) ──")
            if right_arm and left_arm:
                add(f"# 腕を広げるポーズ")
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{right_arm}", 0, 0, -60)')
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{left_arm}",  0, 0,  60)')
                add("time.sleep(1.5)")
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{right_arm}", 0, 0, 0)')
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{left_arm}",  0, 0, 0)')
            else:
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{selected_bone}", 20, 0, 0)')
                add("time.sleep(1.0)")
                add(f'ctrl.set_bone_rotation("{avatar_id}", "{selected_bone}", 0, 0, 0)')
            add("time.sleep(0.5)")
            add("")
        else:
            add("# このVRMは Humanoid リグがないためボーン制御不可")
            add("")

        # 移動デモ
        add("# ── 位置移動デモ ─────────────────────────────────")
        add(f'ctrl.move("{avatar_id}",  0.5, 0, 0)   # 右へ 0.5m')
        add("time.sleep(0.8)")
        add(f'ctrl.move("{avatar_id}", -0.5, 0, 0)   # 元に戻す')
        add("time.sleep(0.5)")
        add("")
        add("ctrl.disconnect()")

        return "\n".join(L)


# ======================================================
# 高レベル操作関数
# ======================================================

def cmd_list_ids(ctrl: ArsistRemoteController) -> list:
    """登録済みオブジェクト ID を表示して vrm ID 一覧を返す"""
    print("\n=== 登録済みオブジェクト ID ===")
    try:
        ids = ctrl.get_ids()
        vrm_ids   = ids.get("vrmIds",   [])
        scene_ids = ids.get("sceneIds", [])
        print(f"  VRM    : {', '.join(vrm_ids)   or '(なし)'}")
        print(f"  Scene  : {', '.join(scene_ids) or '(なし)'}")
        return vrm_ids
    except Exception as e:
        print(f"  [ERROR] {e}")
        return []


def run_demo(ctrl: ArsistRemoteController,
             avatar_id: str,
             startup_wait: float = 3.0) -> None:
    """
    VRM の能力を自動検出してアダプティブなデモを実行する。

    ステップ:
      1. ping  で疎通確認
      2. getIds で登録済み ID を確認
      3. getInfo で VRM 能力を取得 (表情・ボーン・現在 Transform)
      4. 能力に応じてコマンドを実行
    """
    print(f"\n{'='*52}")
    print(f"  Arsist Remote Control  —  Adaptive Demo")
    print(f"{'='*52}")

    # 1. ping
    try:
        rtt = ctrl.ping()
        print(f"  Ping  →  {rtt:.0f} ms")
    except Exception:
        print("  Ping  →  N/A (旧ビルドは query 未対応)")

    if startup_wait > 0:
        print(f"  ランタイム初期化待ち {startup_wait:.1f}s ...")
        time.sleep(startup_wait)

    # 2. getIds
    try:
        ids = ctrl.get_ids()
        vrm_ids   = ids.get("vrmIds", [])
        scene_ids = ids.get("sceneIds", [])
        print(f"\n  VRM IDs   : {vrm_ids   or '(なし)'}")
        print(f"  Scene IDs : {scene_ids or '(なし)'}")

        if vrm_ids and avatar_id not in vrm_ids:
            print(f"\n  ⚠  '{avatar_id}' が見つかりません。'{vrm_ids[0]}' を使用します。")
            avatar_id = vrm_ids[0]
        elif not vrm_ids:
            print("  ⚠  VRM が未登録のため scene コマンドのみ実行します。")
    except Exception as e:
        print(f"\n  [INFO] ID 取得スキップ: {e}")
        vrm_ids = []

    # 3. getInfo
    info = None
    try:
        info = ctrl.get_info(avatar_id)
        exprs        = info.get("Expressions", [])
        bones        = info.get("HumanoidBones", [])
        has_humanoid = info.get("HasHumanoid", False)
        pos          = info.get("Position", [0, 0, 0])

        print(f"\n  ── VRM '{avatar_id}' 能力レポート ─────────────")
        print(f"  現在位置   : ({pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})")
        print(f"  表情数     : {len(exprs)}")
        if exprs:
            cut = exprs[:8]
            print(f"    → {', '.join(cut)}{'  ...' if len(exprs) > 8 else ''}")
        print(f"  Humanoid   : {'YES' if has_humanoid else 'NO'}")
        if has_humanoid and bones:
            print(f"  ボーン数   : {len(bones)}")
            print(f"    → {', '.join(bones[:8])}{'  ...' if len(bones) > 8 else ''}")
    except Exception as e:
        print(f"\n  [INFO] 能力検出スキップ: {e}")
        exprs, bones, has_humanoid = [], [], False
        pos = [0.0, 0.0, 1.5]

    print(f"\n  ── コマンド実行 ───────────────────────────────")

    # Step 1: 絶対座標で配置
    print("\n  [1/4] 絶対座標で配置・正面向き")
    ctrl.set_position(avatar_id, float(pos[0]), float(pos[1]), float(pos[2]))
    ctrl.set_rotation(avatar_id, 0, 0, 0)
    ctrl.set_uniform_scale(avatar_id, 1.0)
    time.sleep(1.0)

    # Step 2: 移動デモ
    print("  [2/4] 移動デモ (左右)")
    ctrl.move(avatar_id,  0.5, 0, 0)
    time.sleep(0.8)
    ctrl.move(avatar_id, -0.5, 0, 0)
    time.sleep(0.5)

    # Step 3: 表情制御
    if info:
        PREFERRED = ["Joy", "A", "aa", "Blink", "Neutral", "Angry", "Sorrow",
                     "Fun", "Fcl_ALL_Neutral", "Fcl_ALL_Joy", "Fcl_MTH_A"]
        to_try = [e for e in PREFERRED if e in exprs] or exprs[:3]
        if to_try:
            print(f"  [3/4] 表情デモ → {to_try[:3]}")
            for expr in to_try[:3]:
                ctrl.set_expression(avatar_id, expr, 80)
                time.sleep(0.8)
                ctrl.reset_expressions(avatar_id)
                time.sleep(0.3)
        else:
            print("  [3/4] 表情デモ: BlendShape なし → スキップ")
    else:
        print("  [3/4] 表情デモ: 能力情報なし → スキップ")

    # Step 4: ボーン制御
    if info and has_humanoid:
        right_arm = "RightUpperArm" if "RightUpperArm" in bones else None
        left_arm  = "LeftUpperArm"  if "LeftUpperArm"  in bones else None
        print("  [4/4] ボーンデモ (Humanoid)")
        if right_arm and left_arm:
            print(f"    → 腕を広げるポーズ")
            ctrl.set_bone_rotation(avatar_id, right_arm, 0, 0, -60)
            ctrl.set_bone_rotation(avatar_id, left_arm,  0, 0,  60)
            time.sleep(1.5)
            ctrl.set_bone_rotation(avatar_id, right_arm, 0, 0, 0)
            ctrl.set_bone_rotation(avatar_id, left_arm,  0, 0, 0)
            time.sleep(0.5)
        elif bones:
            head_bone = next((b for b in ["Head", "Neck", "Spine"] if b in bones), bones[0])
            print(f"    → {head_bone} を傾ける")
            ctrl.set_bone_rotation(avatar_id, head_bone, 20, 0, 0)
            time.sleep(1.0)
            ctrl.set_bone_rotation(avatar_id, head_bone, 0, 0, 0)
            time.sleep(0.5)
        else:
            print("    → 利用可能なボーンなし")
    else:
        print("  [4/4] ボーンデモ: Humanoid なし → スキップ")

    print("\n  ✅ デモ完了")


# ======================================================
# CLI エントリポイント
# ======================================================

def main():
    parser = argparse.ArgumentParser(
        description="Arsist Remote Control — VRM・3D オブジェクト制御ツール",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=textwrap.dedent("""
            使用例:
              python Control.py --demo
              python Control.py --list-ids
              python Control.py --generate-sample --avatar-id avatar
              python Control.py --host 192.168.1.5 --demo
        """),
    )

    # 接続設定
    parser.add_argument("--host",     default=os.getenv("ARSIST_WS_HOST", "127.0.0.1"),
                        help="接続先ホスト (default: 127.0.0.1)")
    parser.add_argument("--port",     type=int, default=int(os.getenv("ARSIST_WS_PORT", "8765")),
                        help="ポート番号 (default: 8765)")
    parser.add_argument("--password", default=os.getenv("ARSIST_WS_PASSWORD", "0000"),
                        help="認証パスワード (default: 0000)")
    parser.add_argument("--avatar-id", default=os.getenv("ARSIST_AVATAR_ID", "avatar"),
                        help="制御対象の Asset ID (default: avatar)")
    parser.add_argument("--startup-wait", type=float,
                        default=float(os.getenv("ARSIST_STARTUP_WAIT", "3.0")),
                        help="接続後の初期化待機秒 (default: 3.0)")

    # 動作モード (排他)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--demo",            action="store_true",
                      help="VRM 能力検出 + アダプティブデモを実行 (デフォルト)")
    mode.add_argument("--list-ids",        action="store_true",
                      help="登録済みID一覧を表示")
    mode.add_argument("--generate-sample", action="store_true",
                      help="VRM 能力に合わせた Python サンプルコードを生成して出力")

    args = parser.parse_args()

    ctrl = ArsistRemoteController(
        device_ip=args.host,
        port=args.port,
        password=args.password or None,  # 空文字 → None
    )
    avatar_id = args.avatar_id

    print(f"\nArsist Remote Controller")
    print(f"  Host      : {args.host}:{args.port}")
    print(f"  Avatar ID : {avatar_id}")
    print(f"  Auth      : {'enabled' if args.password else 'none'}")

    try:
        ctrl.connect()

        if args.list_ids:
            cmd_list_ids(ctrl)

        elif args.generate_sample:
            print(f"\n=== '{avatar_id}' の能力検出中 ...")
            time.sleep(1.0)
            try:
                sample = ctrl.generate_sample_script(avatar_id)
                sep = "─" * 60
                print(f"\n{sep}")
                print(sample)
                print(sep)
                print("\n上記コードをコピーして Control.py と同じディレクトリで実行できます。")
            except Exception as e:
                print(f"[ERROR] {e}")

        else:
            # --demo または引数なし
            run_demo(ctrl, avatar_id, startup_wait=args.startup_wait)

    finally:
        ctrl.disconnect()


if __name__ == "__main__":
    main()