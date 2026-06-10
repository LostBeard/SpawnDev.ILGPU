;; Increment 2 of the store-vanish bisect (Seven 2026-06-10).
;; vanish.wat (flat, inline stores) did NOT fire (0/60 @48w). This variant adds the
;; production STRUCTURE around the publication:
;;   - the store run happens inside a CALLED LEAF FUNCTION ($publish), immediately
;;     before its return (production: helper-final publishes then returns),
;;   - followed (in the caller, "kernel" level) by a SAVE-BLOCK-like run of 64
;;     sequential i64 stores to private memory (production: EmitSaveAllLocals),
;;     then the caller returns to the loop ("dispatcher" level) which barriers.
;; Same memory layout + driver as vanish.wat (use WASM=vanish2.wasm).
(module
  (import "env" "memory" (memory 2 16384 shared))
  (import "env" "notify" (func $notify (param i32 i32) (result i32)))

  ;; leaf: publish iteration i to [base..]: sentinel A, 40 plain, 40 atomic, sentinel B
  (func $publish (param $base i32) (param $i i32)
    (local $k i32)
    local.get $base
    local.get $i
    i32.atomic.store          ;; sentinel A
    i32.const 0
    local.set $k
    block $psDone
      loop $ps
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
      loop $as
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
    i32.atomic.store offset=4 ;; sentinel B -- then immediate return (production shape)
  )

  ;; mid ("kernel"): calls $publish, then a save-block-like 64x i64 store run to
  ;; base+1024.., then returns (production: helper returns -> save block -> return r)
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
          ;; POST-BARRIER VERIFY of previous iteration
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

          ;; the structured store run (leaf publish + kernel save-block)
          local.get $base
          local.get $i
          call $kernelStep

          ;; IMMEDIATE VERIFY (back at "dispatcher" level, post-return)
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
