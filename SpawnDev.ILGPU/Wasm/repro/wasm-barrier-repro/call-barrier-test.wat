(module
  ;; ILGPU-exact barrier repro: separate kernel function via `call`.
  ;; Dispatcher calls kernel(tid, phase, dataBase) for each thread.
  ;; Kernel reads neighbor, computes, writes self, returns yield flag.
  ;; Includes exit flag, yield count, all ILGPU barrier mechanics.
  ;;
  ;; Memory layout:
  ;;   [0]   = arrival counter
  ;;   [4]   = generation
  ;;   [8]   = global yield count
  ;;   [12]  = exit flag
  ;;   [16]  = violation counter
  ;;   [20]  = scratch base (for save/restore simulation)
  ;;   [24+] = shared data (totalThreads * 4 bytes)

  (memory (import "env" "memory") 10 10 shared)

  ;; Kernel function (called per-fiber, separate Wasm frame)
  ;; Returns: 0 = done (last phase), 1 = yielded (more phases)
  (func $kernel (param $tid i32) (param $phase i32) (param $numPhases i32)
                (param $dataBase i32) (param $totalThreads i32)
                (param $scratchBase i32) (param $workerIdx i32)
    (result i32)
    (local $neighborVal i32)
    (local $scratchAddr i32)

    ;; Save phase to scratch (simulates ILGPU state save)
    (local.set $scratchAddr
      (i32.add (local.get $scratchBase) (i32.mul (local.get $workerIdx) (i32.const 8))))
    (i32.atomic.store (local.get $scratchAddr) (local.get $phase))
    (i32.atomic.store offset=4 (local.get $scratchAddr) (local.get $tid))

    ;; Read left neighbor (or 0 if tid==0)
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

    ;; Return: 1 if more phases needed, 0 if this is the last
    (i32.lt_u (local.get $phase) (i32.sub (local.get $numPhases) (i32.const 1)))
  )

  ;; Dispatcher (called once per worker)
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
    (local $scratchBase i32)
    (local $r i32)

    (local.set $totalThreads
      (i32.mul (local.get $workerCount) (local.get $threadsPerWorker)))
    ;; Data after fixed header
    (local.set $dataBase (i32.const 24))
    ;; Scratch after data area
    (local.set $scratchBase
      (i32.add (i32.const 24) (i32.mul (local.get $totalThreads) (i32.const 4))))
    (local.set $violAddr (i32.const 16))
    (local.set $threadStart
      (i32.mul (local.get $workerIdx) (local.get $threadsPerWorker)))
    (local.set $threadEnd
      (i32.add (local.get $threadStart) (local.get $threadsPerWorker)))

    ;; Init: write shared[tid] = tid + 1 for this worker's range
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

        ;; === FIBER LOOP: call kernel for each thread ===
        (local.set $anyYielded (i32.const 0))
        (local.set $tid (local.get $threadStart))
        (block $exit_tid
          (loop $loop_tid
            (br_if $exit_tid
              (i32.ge_u (local.get $tid) (local.get $threadEnd)))

            ;; CALL kernel (separate Wasm frame - matches ILGPU)
            (local.set $r
              (call $kernel
                (local.get $tid) (local.get $phase) (local.get $numPhases)
                (local.get $dataBase) (local.get $totalThreads)
                (local.get $scratchBase) (local.get $workerIdx)))

            ;; Accumulate yield
            (if (i32.eq (local.get $r) (i32.const 1))
              (then (local.set $anyYielded (i32.const 1))))

            (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
            (br $loop_tid)))

        ;; === PHASE BARRIER ===
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

        ;; === EXIT FLAG CHECK ===
        (br_if $exit_phases (i32.atomic.load offset=12 (i32.const 0)))

        (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
        (br $loop_phases)))

    ;; === FINAL VERIFICATION ===
    (local.set $tid (local.get $threadStart))
    (block $ev (loop $vl
      (br_if $ev (i32.ge_u (local.get $tid) (local.get $threadEnd)))
      (if (i32.eqz (i32.atomic.load
            (i32.add (local.get $dataBase) (i32.mul (local.get $tid) (i32.const 4)))))
        (then (drop (i32.atomic.rmw.add (local.get $violAddr) (i32.const 1)))))
      (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
      (br $vl)))
  )
)
