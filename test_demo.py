#!/usr/bin/env python3
"""Final demo test with Inspector metadata"""

from python.arsist_controller import ArsistRemoteController
import time

c = ArsistRemoteController("192.168.0.24")
if not c.connect():
    exit(1)

print("[Demo] Setting Joy expression...")
c.set_expression("avatar", "Joy", 100)
time.sleep(1)

print("[Demo] Waving hand...")
c.wave_hand("avatar", right=True)
time.sleep(2)

print("[Demo] Setting angry expression...")  
c.set_expression("avatar", "Angry", 80)
time.sleep(1)

print("[Demo] Resetting expressions...")
c.reset_expressions("avatar")
c.reset_pose("avatar")

print("[✓ Demo Complete!]")
c.disconnect()
