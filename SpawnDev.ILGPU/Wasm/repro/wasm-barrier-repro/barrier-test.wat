(module
  ;; Minimal reproduction of ILGPU Wasm phase barrier pattern.
  ;; Uses ILGPU's reset-based arrival counter.
  ;; FIXED: Double barrier to prevent write-during-verify race.
  ;;   write -> barrier1 -> verify -> barrier2 -> phase++ -> write -> ...
  ;;
  ;; Memory layout (shared):
  ;;   [0]    = barrier1 arrival counter
  ;;   [4]    = barrier1 generation
  ;;   [8]    = barrier2 arrival counter
  ;;   [12]   = barrier2 generation
  ;;   [16..16+N*4-1] = data area
  ;;   [16+N*4]       = violation count

  (memory (import "env" "memory") 1 1 shared)

  (func (export "run") (param $workerIdx i32) (param $workerCount i32) (param $numPhases i32) (param $useWait32 i32)
    (local $phase i32)
    (local $savedGen i32)
    (local $arrived i32)
    (local $i i32)
    (local $expected i32)
    (local $actual i32)
    (local $dataBase i32)
    (local $violationAddr i32)

    (local.set $dataBase (i32.const 16))
    (local.set $violationAddr
      (i32.add (i32.const 16) (i32.mul (local.get $workerCount) (i32.const 4))))

    (local.set $phase (i32.const 0))
    (block $exit
      (loop $lp
        (br_if $exit (i32.ge_u (local.get $phase) (local.get $numPhases)))

        ;; === WRITE: data[workerIdx] = workerIdx*1000 + phase ===
        (i32.atomic.store
          (i32.add (local.get $dataBase) (i32.mul (local.get $workerIdx) (i32.const 4)))
          (i32.add (i32.mul (local.get $workerIdx) (i32.const 1000)) (local.get $phase)))

        ;; === BARRIER 1 (offset 0,4): sync after writes ===
        (atomic.fence)
        (local.set $savedGen (i32.atomic.load offset=4 (i32.const 0)))
        (local.set $arrived (i32.add (i32.atomic.rmw.add (i32.const 0) (i32.const 1)) (i32.const 1)))
        (if (i32.eq (local.get $arrived) (local.get $workerCount))
          (then
            (i32.atomic.store (i32.const 0) (i32.const 0))
            (atomic.fence)
            (i32.atomic.store offset=4 (i32.const 0) (i32.add (local.get $savedGen) (i32.const 1)))
            (if (local.get $useWait32)
              (then (drop (memory.atomic.notify offset=4 (i32.const 0) (i32.const 2147483647))))))
          (else
            (if (local.get $useWait32)
              (then
                (block $bw (loop $lw
                  (br_if $bw (i32.ne (i32.atomic.load offset=4 (i32.const 0)) (local.get $savedGen)))
                  (drop (memory.atomic.wait32 offset=4 (i32.const 0) (local.get $savedGen) (i64.const -1)))
                  (br $lw))))
              (else
                (block $bs (loop $ls
                  (br_if $bs (i32.ne (i32.atomic.load offset=4 (i32.const 0)) (local.get $savedGen)))
                  (br $ls)))))))
        (atomic.fence)

        ;; === VERIFY: all workers' data correct ===
        (local.set $i (i32.const 0))
        (block $ev (loop $vl
          (br_if $ev (i32.ge_u (local.get $i) (local.get $workerCount)))
          (local.set $expected (i32.add (i32.mul (local.get $i) (i32.const 1000)) (local.get $phase)))
          (local.set $actual (i32.atomic.load
            (i32.add (local.get $dataBase) (i32.mul (local.get $i) (i32.const 4)))))
          (if (i32.ne (local.get $actual) (local.get $expected))
            (then (drop (i32.atomic.rmw.add (local.get $violationAddr) (i32.const 1)))))
          (local.set $i (i32.add (local.get $i) (i32.const 1)))
          (br $vl)))

        ;; === BARRIER 2 (offset 8,12): sync after verify, before next write ===
        (atomic.fence)
        (local.set $savedGen (i32.atomic.load offset=12 (i32.const 0)))
        (local.set $arrived (i32.add (i32.atomic.rmw.add offset=8 (i32.const 0) (i32.const 1)) (i32.const 1)))
        (if (i32.eq (local.get $arrived) (local.get $workerCount))
          (then
            (i32.atomic.store offset=8 (i32.const 0) (i32.const 0))
            (atomic.fence)
            (i32.atomic.store offset=12 (i32.const 0) (i32.add (local.get $savedGen) (i32.const 1)))
            (if (local.get $useWait32)
              (then (drop (memory.atomic.notify offset=12 (i32.const 0) (i32.const 2147483647))))))
          (else
            (if (local.get $useWait32)
              (then
                (block $bw2 (loop $lw2
                  (br_if $bw2 (i32.ne (i32.atomic.load offset=12 (i32.const 0)) (local.get $savedGen)))
                  (drop (memory.atomic.wait32 offset=12 (i32.const 0) (local.get $savedGen) (i64.const -1)))
                  (br $lw2))))
              (else
                (block $bs2 (loop $ls2
                  (br_if $bs2 (i32.ne (i32.atomic.load offset=12 (i32.const 0)) (local.get $savedGen)))
                  (br $ls2)))))))
        (atomic.fence)

        (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
        (br $lp)))
  )
)
