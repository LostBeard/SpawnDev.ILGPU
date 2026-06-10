(module
  ;; Fiber-model barrier repro matching ILGPU's phase dispatcher pattern.
  ;; Each worker runs threadsPerWorker fibers per phase, then barrier.
  ;; Includes yield count + exit flag pattern from ILGPU.
  ;;
  ;; Memory layout (shared):
  ;;   [0]   = phase arrival counter
  ;;   [4]   = phase generation
  ;;   [8]   = global yield count
  ;;   [12]  = exit flag (1 = all done, 0 = more phases)
  ;;   [16]  = group arrival counter
  ;;   [20]  = group generation
  ;;   [24]  = violation counter
  ;;   [28]  = reserved
  ;;   [32+] = shared data (threadsPerWorker * workerCount * 4 bytes)

  (memory (import "env" "memory") 10 10 shared)

  ;; run(workerIdx, workerCount, threadsPerWorker, numPhases, useWait32)
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
    (local $i i32)
    (local $expected i32)
    (local $actual i32)
    (local $totalThreads i32)
    (local $yieldCount i32)

    ;; Compute thread range for this worker
    (local.set $threadStart
      (i32.mul (local.get $workerIdx) (local.get $threadsPerWorker)))
    (local.set $threadEnd
      (i32.add (local.get $threadStart) (local.get $threadsPerWorker)))
    (local.set $totalThreads
      (i32.mul (local.get $workerCount) (local.get $threadsPerWorker)))
    (local.set $dataBase (i32.const 32))
    (local.set $violAddr (i32.const 24))

    (local.set $phase (i32.const 0))
    (block $exit_phases
      (loop $loop_phases
        (br_if $exit_phases
          (i32.ge_u (local.get $phase) (local.get $numPhases)))

        ;; === FIBER LOOP: run all threads in this worker's range ===
        ;; Each thread writes: data[tid] = tid * 1000 + phase
        (local.set $anyYielded (i32.const 0))
        (local.set $tid (local.get $threadStart))
        (block $exit_tid
          (loop $loop_tid
            (br_if $exit_tid
              (i32.ge_u (local.get $tid) (local.get $threadEnd)))

            ;; Write to shared data
            (i32.atomic.store
              (i32.add (local.get $dataBase)
                (i32.mul (local.get $tid) (i32.const 4)))
              (i32.add
                (i32.mul (local.get $tid) (i32.const 1000))
                (local.get $phase)))

            ;; Simulate: if this is the last phase, kernel returns 0 (done)
            ;; otherwise returns 1 (yielded / more phases needed)
            (if (i32.lt_u (local.get $phase)
                  (i32.sub (local.get $numPhases) (i32.const 1)))
              (then (local.set $anyYielded (i32.const 1))))

            (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
            (br $loop_tid)))

        ;; === PHASE BARRIER (matches ILGPU GeneratePhaseDispatcher) ===

        ;; Add this worker's yield to global yield count
        (drop (i32.atomic.rmw.add offset=8 (i32.const 0) (local.get $anyYielded)))

        ;; Pre-barrier fence
        (atomic.fence)

        ;; Save current generation
        (local.set $savedGen (i32.atomic.load offset=4 (i32.const 0)))

        ;; Arrive
        (local.set $arrived
          (i32.add (i32.atomic.rmw.add (i32.const 0) (i32.const 1)) (i32.const 1)))

        ;; Last worker?
        (if (i32.eq (local.get $arrived) (local.get $workerCount))
          (then
            ;; Read yield count, compute exit flag
            (local.set $yieldCount (i32.atomic.load offset=8 (i32.const 0)))
            (i32.atomic.store offset=12 (i32.const 0)
              (i32.eqz (local.get $yieldCount)))

            ;; Reset arrival counter
            (i32.atomic.store (i32.const 0) (i32.const 0))
            ;; Reset yield count
            (i32.atomic.store offset=8 (i32.const 0) (i32.const 0))

            ;; Fence before gen bump
            (atomic.fence)

            ;; Bump generation
            (i32.atomic.store offset=4 (i32.const 0)
              (i32.add (local.get $savedGen) (i32.const 1)))

            ;; Notify if wait32 mode
            (if (local.get $useWait32)
              (then
                (drop (memory.atomic.notify offset=4
                  (i32.const 0) (i32.const 2147483647))))))
          (else
            ;; Non-last: wait or spin
            (if (local.get $useWait32)
              (then
                ;; wait32 with spurious wakeup defense
                (block $bw (loop $lw
                  (br_if $bw (i32.ne
                    (i32.atomic.load offset=4 (i32.const 0))
                    (local.get $savedGen)))
                  (drop (memory.atomic.wait32 offset=4
                    (i32.const 0) (local.get $savedGen) (i64.const -1)))
                  (br $lw))))
              (else
                ;; spin
                (block $bs (loop $ls
                  (br_if $bs (i32.ne
                    (i32.atomic.load offset=4 (i32.const 0))
                    (local.get $savedGen)))
                  (br $ls)))))))

        ;; Post-barrier fence (all workers)
        (atomic.fence)

        ;; === CHECK EXIT FLAG (matches ILGPU pattern) ===
        (br_if $exit_phases (i32.atomic.load offset=12 (i32.const 0)))

        ;; === VERIFY: all threads' data is correct ===
        (local.set $i (i32.const 0))
        (block $ev (loop $vl
          (br_if $ev (i32.ge_u (local.get $i) (local.get $totalThreads)))
          (local.set $expected
            (i32.add (i32.mul (local.get $i) (i32.const 1000)) (local.get $phase)))
          (local.set $actual
            (i32.atomic.load
              (i32.add (local.get $dataBase) (i32.mul (local.get $i) (i32.const 4)))))
          (if (i32.ne (local.get $actual) (local.get $expected))
            (then (drop (i32.atomic.rmw.add (local.get $violAddr) (i32.const 1)))))
          (local.set $i (i32.add (local.get $i) (i32.const 1)))
          (br $vl)))

        (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
        (br $loop_phases)))
  )
)
