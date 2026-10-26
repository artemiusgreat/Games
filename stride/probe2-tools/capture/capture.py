import os, subprocess, sys, time, signal

# usage: capture.py <bin_dir> <phase_file> <out_dir> <timeout_s> <game args...>
# captures: env CAPTURE_PLAN="idle:1.2:idle.png,walk:4.3:walk.png,walk:4.7:walk2.png"
bin_dir, phase_file, out_dir, timeout = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
game_args = sys.argv[5:]
plan = []
for part in os.environ["CAPTURE_PLAN"].split(","):
    ph, el, name = part.split(":")
    plan.append((ph, float(el), name))
os.makedirs(out_dir, exist_ok=True)
if os.path.exists(phase_file): os.remove(phase_file)
env = dict(os.environ)
proc = subprocess.Popen(["./StridePoc.Linux"] + game_args, cwd=bin_dir, env=env,
                        stdout=open(os.path.join(out_dir, "game.log"), "w"), stderr=subprocess.STDOUT)
print(f"game pid={proc.pid}")
peak_rss = 0
done = set()
start = time.time()
try:
    while time.time() - start < timeout:
        if proc.poll() is not None:
            print(f"game exited rc={proc.returncode}")
            break
        try:
            with open(f"/proc/{proc.pid}/status") as f:
                for line in f:
                    if line.startswith("VmHWM:"):
                        peak_rss = max(peak_rss, int(line.split()[1]))
        except FileNotFoundError:
            pass
        phase, elapsed = None, -1
        try:
            txt = open(phase_file).read().split()
            phase, elapsed = txt[0], float(txt[1])
        except Exception:
            pass
        if phase:
            for i, (ph, el, name) in enumerate(plan):
                if i not in done and phase == ph and elapsed >= el:
                    time.sleep(0.05)
                    out = os.path.join(out_dir, name)
                    r = subprocess.run(["import", "-window", "root", out], env=env, capture_output=True, text=True)
                    print(f"captured {name} at phase={phase} t={elapsed} rc={r.returncode} {r.stderr.strip()}")
                    done.add(i)
            if phase == "done":
                print("phase=done, stopping")
                break
        time.sleep(0.1)
finally:
    if proc.poll() is None:
        proc.send_signal(signal.SIGKILL)
    print(f"peak_rss_kb={peak_rss}")
    print(f"captured={len(done)}/{len(plan)}")
