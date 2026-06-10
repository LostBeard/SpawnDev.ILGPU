(module
  (memory (import "env" "memory") 1 1 shared)

  (func (export "run") (param $workerIdx i32) (param $workerCount i32) (param $numPhases i32) (param $useWait32 i32)
    (local $phase i32)
    (local $savedGen i32)
    (local $arrived i32)
    ;; DUMMY LOCALS - pad to target count
    (local $d0 i32) (local $d1 i32) (local $d2 i32) (local $d3 i32) (local $d4 i32)
    (local $d5 i32) (local $d6 i32) (local $d7 i32) (local $d8 i32) (local $d9 i32)
    (local $d10 i32) (local $d11 i32) (local $d12 i32) (local $d13 i32) (local $d14 i32)
    (local $d15 i32) (local $d16 i32) (local $d17 i32) (local $d18 i32) (local $d19 i32)
    (local $d20 i32) (local $d21 i32) (local $d22 i32) (local $d23 i32) (local $d24 i32)
    (local $d25 i32) (local $d26 i32) (local $d27 i32) (local $d28 i32) (local $d29 i32)
    ;; Total: 4 params + 3 real locals + 30 dummy = 37 (above dispatcher's 34)

    ;; Use dummy locals so compiler doesn't optimize them away
    (local.set $d0 (local.get $workerIdx))
    (local.set $d1 (i32.add (local.get $d0) (i32.const 1)))
    (local.set $d2 (i32.add (local.get $d1) (i32.const 1)))
    (local.set $d3 (i32.add (local.get $d2) (i32.const 1)))
    (local.set $d4 (i32.add (local.get $d3) (i32.const 1)))
    (local.set $d5 (i32.add (local.get $d4) (i32.const 1)))
    (local.set $d6 (i32.add (local.get $d5) (i32.const 1)))
    (local.set $d7 (i32.add (local.get $d6) (i32.const 1)))
    (local.set $d8 (i32.add (local.get $d7) (i32.const 1)))
    (local.set $d9 (i32.add (local.get $d8) (i32.const 1)))
    (local.set $d10 (i32.add (local.get $d9) (i32.const 1)))
    (local.set $d11 (i32.add (local.get $d10) (i32.const 1)))
    (local.set $d12 (i32.add (local.get $d11) (i32.const 1)))
    (local.set $d13 (i32.add (local.get $d12) (i32.const 1)))
    (local.set $d14 (i32.add (local.get $d13) (i32.const 1)))
    (local.set $d15 (i32.add (local.get $d14) (i32.const 1)))
    (local.set $d16 (i32.add (local.get $d15) (i32.const 1)))
    (local.set $d17 (i32.add (local.get $d16) (i32.const 1)))
    (local.set $d18 (i32.add (local.get $d17) (i32.const 1)))
    (local.set $d19 (i32.add (local.get $d18) (i32.const 1)))
    (local.set $d20 (i32.add (local.get $d19) (i32.const 1)))
    (local.set $d21 (i32.add (local.get $d20) (i32.const 1)))
    (local.set $d22 (i32.add (local.get $d21) (i32.const 1)))
    (local.set $d23 (i32.add (local.get $d22) (i32.const 1)))
    (local.set $d24 (i32.add (local.get $d23) (i32.const 1)))
    (local.set $d25 (i32.add (local.get $d24) (i32.const 1)))
    (local.set $d26 (i32.add (local.get $d25) (i32.const 1)))
    (local.set $d27 (i32.add (local.get $d26) (i32.const 1)))
    (local.set $d28 (i32.add (local.get $d27) (i32.const 1)))
    (local.set $d29 (i32.add (local.get $d28) (i32.const 1)))

    ;; Write accumulated value to shared[workerIdx] 
    (i32.atomic.store (i32.add (i32.const 8) (i32.mul (local.get $workerIdx) (i32.const 4)))
      (local.get $d29))

    (local.set $phase (i32.const 0))
    (block $exit
      (loop $lp
        (br_if $exit (i32.ge_u (local.get $phase) (local.get $numPhases)))

        ;; Work: accumulate dummy locals into data
        (local.set $d0 (i32.add (local.get $d29) (local.get $phase)))
        (i32.atomic.store (i32.add (i32.const 8) (i32.mul (local.get $workerIdx) (i32.const 4)))
          (local.get $d0))

        ;; Barrier
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

        ;; Verify dummy locals survived the barrier
        ;; d29 should still be workerIdx + 30, d0 should be d29 + phase
        (if (i32.ne (local.get $d29) (i32.add (local.get $workerIdx) (i32.const 30)))
          (then (drop (i32.atomic.rmw.add (i32.add (i32.const 8) (i32.mul (local.get $workerCount) (i32.const 4))) (i32.const 1)))))

        (local.set $phase (i32.add (local.get $phase) (i32.const 1)))
        (br $lp))))
)
