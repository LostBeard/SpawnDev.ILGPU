;; Increment 3 of the store-vanish bisect (Seven 2026-06-10).
;; vanish2 (call structure + save block) did NOT fire. This adds the two remaining
;; production signatures:
;;   1. The EXACT failing publication shape: plain stores fill a temp region, then a
;;      memory->memory ATOMIC COPY publishes it (atomic.load[temp+k] -> atomic.store[out+k])
;;      - production's `[outPtr+4] = atomic.load([structTemp+4])` pair, x40.
;;   2. A 6-TID loop between barriers: each worker runs 6 leaf calls against 6 distinct
;;      private bases per iteration (production tid loop; victim was ALWAYS tid 5 of 6).
;; Layout: worker w, tid t -> base = 65536 + (w*6+t)*4096
;;   base+0 sentinelA, +4 sentinelB, +8..168 plain temp (40), +168..328 atomic out (40),
;;   +1024.. save block. flog per WORKER (4096 + w*64); firstSlot encodes t*100+k.
;; Driver: TIDS=6 WASM=vanish3.wasm (pages calc handles TIDS).
(module
  (import "env" "memory" (memory 2 16384 shared))
  (import "env" "notify" (func $notify (param i32 i32) (result i32)))

  ;; leaf: fill temp (plain), publish via atomic mem->mem copy, sentinels, return
  (func $publish (param $base i32) (param $i i32)
    (local $k i32)
    local.get $base
    local.get $i
    i32.atomic.store          ;; sentinel A
    i32.const 0
    local.set $k
    block $psDone
      loop $ps                ;; 40 PLAIN stores -> temp region
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
    block $cpDone
      loop $cp                ;; 40 ATOMIC mem->mem copies temp -> out (THE failing shape)
        local.get $k
        i32.const 40
        i32.ge_u
        br_if $cpDone
        local.get $base
        i32.const 168
        i32.add
        local.get $k
        i32.const 4
        i32.mul
        i32.add
        local.get $base
        i32.const 8
        i32.add
        local.get $k
        i32.const 4
        i32.mul
        i32.add
        i32.atomic.load
        i32.atomic.store
        local.get $k
        i32.const 1
        i32.add
        local.set $k
        br $cp
      end
    end
    local.get $base
    local.get $i
    i32.atomic.store offset=4 ;; sentinel B, then immediate return
  )

  ;; mid ("kernel"): publish + save-block + return
  (func $kernelStep (param $base i32) (param $i i32)
    (local $k i32)
    local.get $base
    local.get $i
    call $publish
    i32.const 0
    local.set $k
    block $svDone
      loop $sv
        local.get $k
        i32.const 64
        i32.ge_u
        br_if $svDone
        local.get $base
        i32.const 1024
        i32.add
        local.get $k
        i32.const 8
        i32.mul
        i32.add
        local.get $i
        i64.extend_i32_u
        i64.store
        local.get $k
        i32.const 1
        i32.add
        local.set $k
        br $sv
      end
    end
  )

  ;; verify all 6 tid regions of this worker against $exp; log into flog with kindBase
  (func $verify (param $wbase i32) (param $exp i32) (param $flog i32) (param $kindBase i32)
    (local $t i32) (local $k i32) (local $v i32) (local $base i32)
    i32.const 0
    local.set $t
    block $tDone
      loop $tv
        local.get $t
        i32.const 6
        i32.ge_u
        br_if $tDone
        local.get $wbase
        local.get $t
        i32.const 4096
        i32.mul
        i32.add
        local.set $base
        i32.const 0
        local.set $k
        block $kDone
          loop $kv
            local.get $k
            i32.const 80
            i32.ge_u
            br_if $kDone
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
              ;; count at flog+0 (imm) or flog+4 (post) selected by kindBase (0 or 2)
              local.get $flog
              local.get $kindBase
              i32.const 2
              i32.mul
              i32.add
              local.get $flog
              local.get $kindBase
              i32.const 2
              i32.mul
              i32.add
              i32.atomic.load
              i32.const 1
              i32.add
              i32.atomic.store
              local.get $flog
              i32.atomic.load offset=8
              i32.eqz
              if
                local.get $flog
                local.get $exp
                i32.atomic.store offset=8
                local.get $flog
                local.get $t
                i32.const 100
                i32.mul
                local.get $k
                i32.add
                i32.atomic.store offset=12
                local.get $flog
                local.get $v
                i32.atomic.store offset=16
                local.get $flog
                local.get $kindBase
                local.get $k
                i32.const 40
                i32.ge_u
                i32.add
                i32.atomic.store offset=20
              end
            end
            local.get $k
            i32.const 1
            i32.add
            local.set $k
            br $kv
          end
        end
        local.get $t
        i32.const 1
        i32.add
        local.set $t
        br $tv
      end
    end
  )

  (func (export "run")
        (param $wid i32) (param $wc i32) (param $iters i32) (param $spinMax i32)
        (param $resume i32)
        (result i32)
    (local $i i32) (local $g i32) (local $arrived i32) (local $spins i32)
    (local $t i32) (local $wbase i32) (local $ystate i32) (local $flog i32)

    local.get $wid
    i32.const 24576
    i32.mul
    i32.const 65536
    i32.add
    local.set $wbase
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

        local.get $resume
        i32.eqz
        if
          ;; POST-BARRIER VERIFY of previous iteration (all 6 tids)
          local.get $i
          i32.const 0
          i32.gt_u
          if
            local.get $wbase
            local.get $i
            i32.const 1
            i32.sub
            local.get $flog
            i32.const 2
            call $verify
          end

          ;; TID LOOP: 6 leaf calls (production tid loop)
          i32.const 0
          local.set $t
          block $tlDone
            loop $tl
              local.get $t
              i32.const 6
              i32.ge_u
              br_if $tlDone
              local.get $wbase
              local.get $t
              i32.const 4096
              i32.mul
              i32.add
              local.get $i
              call $kernelStep
              local.get $t
              i32.const 1
              i32.add
              local.set $t
              br $tl
            end
          end

          ;; IMMEDIATE VERIFY (all 6 tids)
          local.get $wbase
          local.get $i
          local.get $flog
          i32.const 0
          call $verify

          ;; BARRIER
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
            i32.const 0
            local.set $spins
          else
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
    local.get $ystate
    i32.const 0
    i32.store
    i32.const 0
  )
)
