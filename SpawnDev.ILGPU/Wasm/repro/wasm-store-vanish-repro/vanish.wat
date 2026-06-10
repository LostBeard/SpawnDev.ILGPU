;; Minimal repro candidate for the store-vanish anomaly (Seven 2026-06-10).
;; Mirrors the production dispatcher's shape with ZERO ILGPU machinery:
;;   per iteration, each worker writes a sentinel-bracketed RUN of plain + atomic
;;   stores to its PRIVATE region, immediately verifies them (same thread), crosses
;;   a generation barrier (arrival rmw + seq_cst gen spin + yield-to-JS escape +
;;   JS park/re-enter, exactly the production protocol), then verifies the previous
;;   iteration's stores again POST-barrier (the production consumption position).
;; Failure = any slot != expected while both sentinels are correct -> logged.
;;
;; Memory layout (shared, imported):
;;   0:    barrier arrival (i32, atomic)
;;   4:    barrier generation (i32, atomic)
;;   64+:  yieldState per worker, 16B stride: {flag@0, savedGen@4, iter@8}
;;   4096+: failure log per worker, 64B stride:
;;          {immCount@0, postCount@4, firstIter@8, firstSlotOff@12, firstVal@16, firstKind@20}
;;          kind: 0=plain-imm 1=atomic-imm 2=plain-post 3=atomic-post
;;   65536+: data per worker, 4096B stride:
;;          {sentinelA@0, sentinelB@4, plain[40]@8, atomic[40]@168}
(module
  (import "env" "memory" (memory 2 16384 shared))
  (import "env" "notify" (func $notify (param i32 i32) (result i32)))

  (func (export "run")
        (param $wid i32) (param $wc i32) (param $iters i32) (param $spinMax i32)
        (param $resume i32)
        (result i32)
    (local $i i32) (local $g i32) (local $arrived i32) (local $spins i32)
    (local $k i32) (local $base i32) (local $ystate i32) (local $flog i32)
    (local $v i32) (local $exp i32)

    local.get $wid
    i32.const 4096
    i32.mul
    i32.const 65536
    i32.add
    local.set $base
    local.get $wid
    i32.const 16
    i32.mul
    i32.const 64
    i32.add
    local.set $ystate
    local.get $wid
    i32.const 64
    i32.mul
    i32.const 4096
    i32.add
    local.set $flog

    ;; resume: restore iter + savedGen, jump into the spin
    local.get $resume
    if
      local.get $ystate
      i32.load offset=8
      local.set $i
      local.get $ystate
      i32.load offset=4
      local.set $g
    else
      i32.const 0
      local.set $i
    end

    block $done
      loop $iter
        local.get $i
        local.get $iters
        i32.ge_u
        br_if $done

        ;; ===== on resume, skip stores+arrival (already done pre-yield): go spin =====
        local.get $resume
        i32.eqz
        if
          ;; ----- POST-BARRIER VERIFY of previous iteration (slots must == i-1) -----
          local.get $i
          i32.const 0
          i32.gt_u
          if
            local.get $i
            i32.const 1
            i32.sub
            local.set $exp
            i32.const 0
            local.set $k
            block $pvDone
              loop $pv
                local.get $k
                i32.const 80
                i32.ge_u
                br_if $pvDone
                ;; slot addr: base+8+k*4 (k<40 plain, k>=40 atomic at 168+(k-40)*4 = 8+k*4+(k>=40?0:0)) -- contiguous: 8..168 plain, 168..328 atomic; 8+k*4 covers both
                local.get $base
                i32.const 8
                i32.add
                local.get $k
                i32.const 4
                i32.mul
                i32.add
                i32.atomic.load
                local.set $v
                local.get $v
                local.get $exp
                i32.ne
                if
                  local.get $flog
                  local.get $flog
                  i32.atomic.load offset=4
                  i32.const 1
                  i32.add
                  i32.atomic.store offset=4
                  ;; record first
                  local.get $flog
                  i32.atomic.load offset=8
                  i32.eqz
                  if
                    local.get $flog
                    local.get $i
                    i32.atomic.store offset=8
                    local.get $flog
                    local.get $k
                    i32.atomic.store offset=12
                    local.get $flog
                    local.get $v
                    i32.atomic.store offset=16
                    local.get $flog
                    local.get $k
                    i32.const 40
                    i32.lt_u
                    if (result i32)
                      i32.const 2
                    else
                      i32.const 3
                    end
                    i32.atomic.store offset=20
                  end
                end
                local.get $k
                i32.const 1
                i32.add
                local.set $k
                br $pv
              end
            end
          end

          ;; ----- STORE RUN for iteration i -----
          local.get $base
          local.get $i
          i32.atomic.store          ;; sentinel A
          i32.const 0
          local.set $k
          block $psDone
            loop $ps               ;; 40 PLAIN stores
              local.get $k
              i32.const 40
              i32.ge_u
              br_if $psDone
              local.get $base
              i32.const 8
              i32.add
              local.get $k
              i32.const 4
              i32.mul
              i32.add
              local.get $i
              i32.store
              local.get $k
              i32.const 1
              i32.add
              local.set $k
              br $ps
            end
          end
          i32.const 0
          local.set $k
          block $asDone
            loop $as               ;; 40 ATOMIC stores
              local.get $k
              i32.const 40
              i32.ge_u
              br_if $asDone
              local.get $base
              i32.const 168
              i32.add
              local.get $k
              i32.const 4
              i32.mul
              i32.add
              local.get $i
              i32.atomic.store
              local.get $k
              i32.const 1
              i32.add
              local.set $k
              br $as
            end
          end
          local.get $base
          local.get $i
          i32.atomic.store offset=4 ;; sentinel B

          ;; ----- IMMEDIATE VERIFY (same thread, right after the run) -----
          i32.const 0
          local.set $k
          block $ivDone
            loop $iv
              local.get $k
              i32.const 80
              i32.ge_u
              br_if $ivDone
              local.get $base
              i32.const 8
              i32.add
              local.get $k
              i32.const 4
              i32.mul
              i32.add
              i32.atomic.load
              local.set $v
              local.get $v
              local.get $i
              i32.ne
              if
                local.get $flog
                local.get $flog
                i32.atomic.load
                i32.const 1
                i32.add
                i32.atomic.store
                local.get $flog
                i32.atomic.load offset=8
                i32.eqz
                if
                  local.get $flog
                  local.get $i
                  i32.atomic.store offset=8
                  local.get $flog
                  local.get $k
                  i32.atomic.store offset=12
                  local.get $flog
                  local.get $v
                  i32.atomic.store offset=16
                  local.get $flog
                  local.get $k
                  i32.const 40
                  i32.lt_u
                  if (result i32)
                    i32.const 0
                  else
                    i32.const 1
                  end
                  i32.atomic.store offset=20
                end
              end
              local.get $k
              i32.const 1
              i32.add
              local.set $k
              br $iv
            end
          end

          ;; ----- BARRIER: arrive (production shape) -----
          i32.const 4
          i32.atomic.load
          local.set $g
          i32.const 0
          i32.const 1
          i32.atomic.rmw.add
          i32.const 1
          i32.add
          local.set $arrived
          local.get $arrived
          local.get $wc
          i32.eq
          if
            ;; last arriver: reset arrival, fence, bump gen, notify
            i32.const 0
            i32.const 0
            i32.atomic.store
            atomic.fence
            i32.const 4
            local.get $g
            i32.const 1
            i32.add
            i32.atomic.store
            i32.const 4
            i32.const 2147483647
            call $notify
            drop
            ;; falls through past the spin (it is the releaser)
            i32.const 0
            local.set $spins
          else
            ;; waiter: spin with yield escape
            i32.const 0
            local.set $spins
            block $spun
              loop $spin
                i32.const 4
                i32.atomic.load
                local.get $g
                i32.ne
                br_if $spun
                local.get $spins
                i32.const 1
                i32.add
                local.set $spins
                local.get $spins
                local.get $spinMax
                i32.gt_u
                if
                  ;; yield to JS: save {flag=1, savedGen, iter}
                  local.get $ystate
                  i32.const 1
                  i32.store
                  local.get $ystate
                  local.get $g
                  i32.store offset=4
                  local.get $ystate
                  local.get $i
                  i32.store offset=8
                  i32.const 1
                  return
                end
                br $spin
              end
            end
          end
        else
          ;; ===== RESUMED: re-enter the spin with restored g =====
          i32.const 0
          local.set $resume
          i32.const 0
          local.set $spins
          block $spun2
            loop $spin2
              i32.const 4
              i32.atomic.load
              local.get $g
              i32.ne
              br_if $spun2
              local.get $spins
              i32.const 1
              i32.add
              local.set $spins
              local.get $spins
              local.get $spinMax
              i32.gt_u
              if
                local.get $ystate
                i32.const 1
                i32.store
                local.get $ystate
                local.get $g
                i32.store offset=4
                local.get $ystate
                local.get $i
                i32.store offset=8
                i32.const 1
                return
              end
              br $spin2
            end
          end
        end

        atomic.fence
        local.get $i
        i32.const 1
        i32.add
        local.set $i
        br $iter
      end
    end
    ;; done: clear yield flag
    local.get $ystate
    i32.const 0
    i32.store
    i32.const 0
  )
)
