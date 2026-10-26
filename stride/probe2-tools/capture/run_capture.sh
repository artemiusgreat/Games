#!/bin/bash
# usage: run_capture.sh <display> <outdir> <phasefile> <plan> <timeout> <game args...>
set -u
DISP=$1; OUT=$2; PHASE=$3; PLAN=$4; TIMEOUT=$5; shift 5
SCRATCH=~/workspace/game-poc/stride/probe2-scratch
export DOTNET_ROOT=~/.dotnet10 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json
export PATH=$HOME/.dotnet10:$PATH
Xvfb $DISP -screen 0 1280x800x24 >$OUT/xvfb.log 2>&1 &
XVFB_PID=$!
sleep 1.5
export DISPLAY=$DISP
CAPTURE_PLAN="$PLAN" python3 $SCRATCH/capture.py ~/workspace/game-poc/stride/StridePoc/Bin/Linux/Release "$PHASE" "$OUT" "$TIMEOUT" "$@"
kill $XVFB_PID 2>/dev/null
echo "RUN_CAPTURE_DONE out=$OUT"
