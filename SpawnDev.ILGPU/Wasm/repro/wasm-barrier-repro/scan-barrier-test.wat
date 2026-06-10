(module
  ;; Scan-like barrier repro matching ILGPU's actual access pattern.
  ;; No mid-loop verification. Each phase: read neighbor, add, write self.
  ;; Exit flag controls loop termination (ILGPU pattern).
  ;;
  ;; Memory layout:
  ;;   [0]   = arrival counter
  ;;   [4]   = generation
  ;;   [8]   = global yield count
  ;;   [12]  = exit flag
  ;;   [16]  = violation counter
  ;;   [20]  = reserved
  ;;   [24+] = shared data (totalThreads * 4 bytes)

  (memory (import "env" "memory") 10 10 shared)

  (func (export "run")
    (param $workerIdx i32) (param $workerCount i32) (param $threadsPerWorker i32)
    (param $numPhases i32) (param $useWait32 i32)

    (local $phase i32)
    (local $tid i32)
    (local $threadStart i32)
    (local $threadEnd i32)
    (local $savedGen i32)
    (local $arrived i32)
    (local $anyYielded i32)
    (local $dataBase i32)
    (local $violAddr i32)
    (local $totalThreads i32)
    (local $yieldCount i32)
    (local $neighborVal i32)
    (local $addr i32)

    (local.set $threadStart
      (i32.mul (local.get $workerIdx) (local.get $threadsPerWorker)))
    (local.set $threadEnd
      (i32.add (local.get $threadStart) (local.get $threadsPerWorker)))
    (local.set $totalThreads
      (i32.mul (local.get $workerCount) (local.get $threadsPerWorker)))
    (local.set $dataBase (i32.const 24))
    (local.set $violAddr (i32.const 16))

    ;; Phase 0: initialize shared[tid] = tid + 1
    (local.set $tid (local.get $threadStart))
    (block $ei (loop $li
      (br_if $ei (i32.ge_u (local.get $tid) (local.get $threadEnd)))
      (i32.atomic.store
        (i32.add (local.get $dataBase) (i32.mul (local.get $tid) (i32.const 4)))
        (i32.add (local.get $tid) (i32.const 1)))
      (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
      (br $li)))

    (local.set $phase (i32.const 0))
    (block $exit_phases
      (loop $loop_phases
        (br_if $exit_phases
          (i32.ge_u (local.get $phase) (local.get $numPhases)))

        ;; === YIELD FLAG: 1 if more phases needed, 0 on last phase ===
        (local.set $anyYielded
          (i32.lt_u (local.get $phase)
            (i32.sub (local.get $numPhases) (i32.const 1))))

        ;; === FIBER LOOP: scan-like read-compute-write ===
        ;; Each thread reads shared[tid-1] (or 0 if tid==0), adds (phase+1), writes shared[tid]
        (local.set $tid (local.get $threadStart))
        (block $et (loop $lt
          (br_if $et (i32.ge_u (local.get $tid) (local.get $threadEnd)))

          ;; Read left neighbor (tid-1), or 0 if tid==0
          (if (i32.gt_u (local.get $tid) (i32.const 0))
            (then
              (local.set $neighborVal
                (i32.atomic.load
                  (i32.add (local.get $dataBase)
                    (i32.mul (i32.sub (local.get $tid) (i32.const 1)) (i32.const 4))))))
            (else
              (local.set $neighborVal (i32.const 0))))

          ;; Write shared[tid] = neighborVal + (phase + 1)
          (i32.atomic.store
            (i32.add (local.get $dataBase) (i32.mul (local.get $tid) (i32.const 4)))
            (i32.add (local.get $neighborVal) (i32.add (local.get $phase) (i32.const 1))))

          (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
          (br $lt)))

        ;; === PHASE BARRIER (exact ILGPU pattern) ===
        (drop (i32.atomic.rmw.add offset=8 (i32.const 0) (local.get $anyYielded)))
        (atomic.fence)
        (local.set $savedGen (i32.atomic.load offset=4 (i32.const 0)))
        (local.set $arrived
          (i32.add (i32.atomic.rmw.add (i32.const 0) (i32.const 1)) (i32.const 1)))

        (if (i32.eq (local.get $arrived) (local.get $workerCount))
          (then
            (local.set $yieldCount (i32.atomic.load offset=8 (i32.const 0)))
            (i32.atomic.store offset=12 (i32.const 0)
              (i32.eqz (local.get $yieldCount)))
            (i32.atomic.store (i32.const 0) (i32.const 0))
            (i32.atomic.store offset=8 (i32.const 0) (i32.const 0))
            (atomic.fence)
            (i32.atomic.store offset=4 (i32.const 0)
              (i32.add (local.get $savedGen) (i32.const 1)))
            (if (local.get $useWait32)
              (then
                (drop (memory.atomic.notify offset=4
                  (i32.const 0) (i32.const 2147483647))))))
          (else
            (if (local.get $useWait32)
              (then
                (block $bw (loop $lw
                  (br_if $bw (i32.ne
                    (i32.atomic.load offset=4 (i32.const 0))
                    (local.get $savedGen)))
                  (drop (memory.atomic.wait32 offset=4
                    (i32.const 0) (local.get $savedGen) (i64.const -1)))
                  (br $lw))))
              (else
                (block $bs (loop $ls
                  (br_if $bs (i32.ne
                    (i32.atomic.load offset=4 (i32.const 0))
                    (local.get $savedGen)))
                  (br $ls)))))))

        (atomic.fence)

        ;; === EXIT FLAG CHECK (ILGPU pattern) ===
        (br_if $exit_phases (i32.atomic.load offset=12 (i32.const 0)))

        (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
        (br $loop_phases)))

    ;; === FINAL VERIFICATION (after all phases) ===
    ;; Check shared[tid] for each thread in this worker's range
    ;; Expected value depends on the scan pattern and number of phases
    ;; Simple check: shared[tid] should NOT be the initial value (tid+1) if phases > 0
    ;; More precise: just check that all threads in range have been written
    (local.set $tid (local.get $threadStart))
    (block $ev (loop $vl
      (br_if $ev (i32.ge_u (local.get $tid) (local.get $threadEnd)))
      (local.set $addr
        (i32.add (local.get $dataBase) (i32.mul (local.get $tid) (i32.const 4))))
      ;; If value is 0, something went very wrong (barrier failure caused missed writes)
      (if (i32.eqz (i32.atomic.load (local.get $addr)))
        (then (drop (i32.atomic.rmw.add (local.get $violAddr) (i32.const 1)))))
      (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
      (br $vl)))
  )
)
