(module
  ;; Multi-helper barrier repro: simulates RadixSort calling scan helpers.
  ;; Kernel has 8 phases (2 helpers x 3 phases each + 2 post-helper barriers).
  ;; Phase 0-2: helper A (write region A of shared mem)
  ;; Phase 3: post-helper barrier (just sync, no work)
  ;; Phase 4-6: helper B (write region B of shared mem)
  ;; Phase 7: post-helper barrier (just sync, no work)
  ;;
  ;; Memory layout:
  ;;   [0]    = arrival counter
  ;;   [4]    = generation
  ;;   [8]    = global yield count
  ;;   [12]   = exit flag
  ;;   [16]   = violation counter
  ;;   [20]   = reserved
  ;;   [24]   = region A (totalThreads * 4 bytes)
  ;;   [24 + totalThreads*4] = region B (totalThreads * 4 bytes)

  (memory (import "env" "memory") 10 10 shared)

  ;; Kernel: returns 1 (yielded/more phases) or 0 (done)
  ;; Simulates a kernel with 2 helpers, each doing 3 phases of scan-like work.
  (func $kernel (param $tid i32) (param $phase i32) (param $totalPhases i32)
                (param $regionA i32) (param $regionB i32) (param $totalThreads i32)
    (result i32)
    (local $neighborVal i32)
    (local $addr i32)
    (local $region i32)
    (local $localPhase i32)

    ;; Determine which helper and local phase
    ;; Phase 0-2: helper A (region A), local phase 0-2
    ;; Phase 3: post-helper barrier (no work, just yield)
    ;; Phase 4-6: helper B (region B), local phase 0-2
    ;; Phase 7: post-helper barrier (no work, just yield)

    (if (i32.le_u (local.get $phase) (i32.const 2))
      (then
        ;; Helper A: phases 0-2, region A
        (local.set $region (local.get $regionA))
        (local.set $localPhase (local.get $phase))

        ;; Read left neighbor from region
        (if (i32.gt_u (local.get $tid) (i32.const 0))
          (then
            (local.set $neighborVal
              (i32.atomic.load
                (i32.add (local.get $region)
                  (i32.mul (i32.sub (local.get $tid) (i32.const 1)) (i32.const 4))))))
          (else
            (local.set $neighborVal (i32.const 0))))

        ;; Write: region[tid] = neighborVal + (localPhase + 1) * 100 + tid
        (i32.atomic.store
          (i32.add (local.get $region) (i32.mul (local.get $tid) (i32.const 4)))
          (i32.add (local.get $neighborVal)
            (i32.add
              (i32.mul (i32.add (local.get $localPhase) (i32.const 1)) (i32.const 100))
              (local.get $tid)))))

    (else (if (i32.eq (local.get $phase) (i32.const 3))
      (then
        ;; Post-helper barrier phase: no work, just sync
        (nop))

    (else (if (i32.le_u (local.get $phase) (i32.const 6))
      (then
        ;; Helper B: phases 4-6, region B
        (local.set $region (local.get $regionB))
        (local.set $localPhase (i32.sub (local.get $phase) (i32.const 4)))

        (if (i32.gt_u (local.get $tid) (i32.const 0))
          (then
            (local.set $neighborVal
              (i32.atomic.load
                (i32.add (local.get $region)
                  (i32.mul (i32.sub (local.get $tid) (i32.const 1)) (i32.const 4))))))
          (else
            (local.set $neighborVal (i32.const 0))))

        (i32.atomic.store
          (i32.add (local.get $region) (i32.mul (local.get $tid) (i32.const 4)))
          (i32.add (local.get $neighborVal)
            (i32.add
              (i32.mul (i32.add (local.get $localPhase) (i32.const 1)) (i32.const 100))
              (local.get $tid)))))

    (else
      ;; Phase 7: post-helper barrier, no work
      (nop)))))))

    ;; Return: 1 if more phases, 0 if this is the last
    (i32.lt_u (local.get $phase) (i32.sub (local.get $totalPhases) (i32.const 1)))
  )

  ;; Dispatcher
  (func (export "run")
    (param $workerIdx i32) (param $workerCount i32) (param $threadsPerWorker i32)
    (param $numGroups i32) (param $useWait32 i32)

    (local $group i32)
    (local $phase i32)
    (local $tid i32)
    (local $threadStart i32)
    (local $threadEnd i32)
    (local $savedGen i32)
    (local $arrived i32)
    (local $anyYielded i32)
    (local $totalThreads i32)
    (local $yieldCount i32)
    (local $r i32)
    (local $regionA i32)
    (local $regionB i32)
    (local $violAddr i32)
    (local $totalPhases i32)

    (local.set $totalPhases (i32.const 8)) ;; 3+1+3+1
    (local.set $totalThreads
      (i32.mul (local.get $workerCount) (local.get $threadsPerWorker)))
    (local.set $regionA (i32.const 24))
    (local.set $regionB
      (i32.add (i32.const 24) (i32.mul (local.get $totalThreads) (i32.const 4))))
    (local.set $violAddr (i32.const 16))
    (local.set $threadStart
      (i32.mul (local.get $workerIdx) (local.get $threadsPerWorker)))
    (local.set $threadEnd
      (i32.add (local.get $threadStart) (local.get $threadsPerWorker)))

    ;; Multi-group loop (matches ILGPU dispatcher)
    (local.set $group (i32.const 0))
    (block $exit_groups
      (loop $loop_groups
        (br_if $exit_groups
          (i32.ge_u (local.get $group) (local.get $numGroups)))

        ;; Init regions for this group
        (local.set $tid (local.get $threadStart))
        (block $ei (loop $li
          (br_if $ei (i32.ge_u (local.get $tid) (local.get $threadEnd)))
          (i32.atomic.store
            (i32.add (local.get $regionA) (i32.mul (local.get $tid) (i32.const 4)))
            (i32.add (local.get $tid) (i32.const 1)))
          (i32.atomic.store
            (i32.add (local.get $regionB) (i32.mul (local.get $tid) (i32.const 4)))
            (i32.add (local.get $tid) (i32.const 1)))
          (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
          (br $li)))

        ;; Phase loop
        (local.set $phase (i32.const 0))
        (block $exit_phases
          (loop $loop_phases

            ;; Fiber loop: call kernel for each thread
            (local.set $anyYielded (i32.const 0))
            (local.set $tid (local.get $threadStart))
            (block $et (loop $lt
              (br_if $et (i32.ge_u (local.get $tid) (local.get $threadEnd)))
              (local.set $r
                (call $kernel
                  (local.get $tid) (local.get $phase) (local.get $totalPhases)
                  (local.get $regionA) (local.get $regionB) (local.get $totalThreads)))
              (if (i32.eq (local.get $r) (i32.const 1))
                (then (local.set $anyYielded (i32.const 1))))
              (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
              (br $lt)))

            ;; Phase barrier
            (drop (i32.atomic.rmw.add offset=8 (i32.const 0) (local.get $anyYielded)))
            (atomic.fence)
            (local.set $savedGen (i32.atomic.load offset=4 (i32.const 0)))
            (local.set $arrived
              (i32.add (i32.atomic.rmw.add (i32.const 0) (i32.const 1)) (i32.const 1)))

            (if (i32.eq (local.get $arrived) (local.get $workerCount))
              (then
                (local.set $yieldCount (i32.atomic.load offset=8 (i32.const 0)))
                (i32.atomic.store offset=12 (i32.const 0) (i32.eqz (local.get $yieldCount)))
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

            ;; Exit flag check
            (br_if $exit_phases (i32.atomic.load offset=12 (i32.const 0)))

            (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
            (br $loop_phases)))

        ;; Group complete - next group
        (local.set $group (i32.add (local.get $group) (i32.const 1)))
        (br $loop_groups)))

    ;; Final verification: regions should have non-zero values
    (local.set $tid (local.get $threadStart))
    (block $ev (loop $vl
      (br_if $ev (i32.ge_u (local.get $tid) (local.get $threadEnd)))
      (if (i32.eqz (i32.atomic.load
            (i32.add (local.get $regionA) (i32.mul (local.get $tid) (i32.const 4)))))
        (then (drop (i32.atomic.rmw.add (local.get $violAddr) (i32.const 1)))))
      (if (i32.eqz (i32.atomic.load
            (i32.add (local.get $regionB) (i32.mul (local.get $tid) (i32.const 4)))))
        (then (drop (i32.atomic.rmw.add (local.get $violAddr) (i32.const 1)))))
      (local.set $tid (i32.add (local.get $tid) (i32.const 1)))
      (br $vl)))
  )
)
