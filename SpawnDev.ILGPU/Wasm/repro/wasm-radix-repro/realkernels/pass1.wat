(module
  (type (;0;) (func (param f64) (result f64)))
  (type (;1;) (func (param f64 f64) (result f64)))
  (type (;2;) (func (param i32 i32) (result i32)))
  (type (;3;) (func (param i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i64 i32) (result i32)))
  (import "env" "memory" (memory (;0;) 1 16384 shared))
  (import "Math" "sin" (func (;0;) (type 0)))
  (import "Math" "cos" (func (;1;) (type 0)))
  (import "Math" "tan" (func (;2;) (type 0)))
  (import "Math" "asin" (func (;3;) (type 0)))
  (import "Math" "acos" (func (;4;) (type 0)))
  (import "Math" "atan" (func (;5;) (type 0)))
  (import "Math" "sinh" (func (;6;) (type 0)))
  (import "Math" "cosh" (func (;7;) (type 0)))
  (import "Math" "tanh" (func (;8;) (type 0)))
  (import "Math" "exp" (func (;9;) (type 0)))
  (import "Math" "log" (func (;10;) (type 0)))
  (import "Math" "log2" (func (;11;) (type 0)))
  (import "Math" "log10" (func (;12;) (type 0)))
  (import "Math" "round" (func (;13;) (type 0)))
  (import "Math" "truncate" (func (;14;) (type 0)))
  (import "Math" "sign" (func (;15;) (type 0)))
  (import "Math" "exp2" (func (;16;) (type 0)))
  (import "Math" "sqrt" (func (;17;) (type 0)))
  (import "Math" "abs" (func (;18;) (type 0)))
  (import "Math" "ceil" (func (;19;) (type 0)))
  (import "Math" "floor" (func (;20;) (type 0)))
  (import "Math" "pow" (func (;21;) (type 1)))
  (import "Math" "atan2" (func (;22;) (type 1)))
  (import "env" "notify" (func (;23;) (type 2)))
  (func (;24;) (type 3) (param i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i64 i32) (result i32)
    (local i32 i32 i32 i64 i32 i32 i64 i64 i64 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i64 i64 i64 i64 i64 i64 i32 i64 i32 i32 i64 i64 i64 i64 i64 i64 i32 i64 i32 i32 i64 i64 i64 i64 i64 i64 i32 i64 i32 i32 i32 i32 i64 i32 i32 i32 i64 i64 i32 i32 i64 i32 i32 i32 i32 i64)
    i32.const 0
    local.set 14
    block  ;; label = @1
      loop  ;; label = @2
        block  ;; label = @3
          block  ;; label = @4
            block  ;; label = @5
              block  ;; label = @6
                block  ;; label = @7
                  block  ;; label = @8
                    block  ;; label = @9
                      local.get 14
                      br_table 0 (;@9;) 1 (;@8;) 2 (;@7;) 3 (;@6;) 4 (;@5;) 5 (;@4;) 6 (;@3;) 8 (;@1;)
                    end
                    local.get 13
                    i64.load
                    i32.wrap_i64
                    local.set 16
                    local.get 13
                    i32.const 8
                    i32.add
                    i64.load
                    local.set 17
                    local.get 13
                    i32.const 24
                    i32.add
                    i32.load
                    local.set 18
                    i32.const 8
                    local.set 19
                    local.get 19
                    i64.extend_i32_s
                    local.set 20
                    local.get 12
                    local.get 20
                    i64.rem_s
                    local.set 21
                    i64.const 0
                    local.set 22
                    local.get 21
                    local.get 22
                    i64.eq
                    local.set 23
                    local.get 0
                    local.get 4
                    i32.div_u
                    local.get 1
                    local.get 10
                    i32.div_u
                    i32.rem_u
                    local.set 24
                    local.get 0
                    local.get 4
                    i32.div_u
                    local.get 1
                    local.get 10
                    i32.div_u
                    i32.div_u
                    local.get 2
                    local.get 11
                    i32.div_u
                    i32.rem_u
                    local.set 25
                    local.get 0
                    local.get 4
                    i32.div_u
                    local.get 1
                    local.get 10
                    i32.div_u
                    local.get 2
                    local.get 11
                    i32.div_u
                    i32.mul
                    i32.div_u
                    local.set 26
                    local.get 5
                    local.get 10
                    i32.rem_u
                    local.set 27
                    local.get 5
                    local.get 10
                    i32.div_u
                    local.get 11
                    i32.rem_u
                    local.set 28
                    local.get 5
                    local.get 10
                    local.get 11
                    i32.mul
                    i32.div_u
                    local.set 29
                    local.get 10
                    local.set 30
                    local.get 11
                    local.set 31
                    local.get 4
                    local.get 10
                    local.get 11
                    i32.mul
                    i32.div_u
                    local.set 32
                    local.get 27
                    i64.extend_i32_s
                    local.set 33
                    local.get 24
                    i64.extend_i32_s
                    local.set 34
                    local.get 30
                    i64.extend_i32_s
                    local.set 35
                    local.get 34
                    local.get 35
                    i64.mul
                    local.set 36
                    local.get 33
                    local.get 36
                    i64.add
                    local.set 37
                    i64.const -2147483648
                    local.set 38
                    local.get 37
                    local.get 38
                    i64.ge_s
                    local.set 39
                    i64.const 2147483647
                    local.set 40
                    local.get 37
                    local.get 40
                    i64.le_s
                    local.set 41
                    local.get 39
                    local.get 41
                    i32.and
                    local.set 42
                    local.get 28
                    i64.extend_i32_s
                    local.set 43
                    local.get 25
                    i64.extend_i32_s
                    local.set 44
                    local.get 31
                    i64.extend_i32_s
                    local.set 45
                    local.get 44
                    local.get 45
                    i64.mul
                    local.set 46
                    local.get 43
                    local.get 46
                    i64.add
                    local.set 47
                    i64.const -2147483648
                    local.set 48
                    local.get 47
                    local.get 48
                    i64.ge_s
                    local.set 49
                    i64.const 2147483647
                    local.set 50
                    local.get 47
                    local.get 50
                    i64.le_s
                    local.set 51
                    local.get 49
                    local.get 51
                    i32.and
                    local.set 52
                    local.get 29
                    i64.extend_i32_s
                    local.set 53
                    local.get 26
                    i64.extend_i32_s
                    local.set 54
                    local.get 32
                    i64.extend_i32_s
                    local.set 55
                    local.get 54
                    local.get 55
                    i64.mul
                    local.set 56
                    local.get 53
                    local.get 56
                    i64.add
                    local.set 57
                    i64.const -2147483648
                    local.set 58
                    local.get 57
                    local.get 58
                    i64.ge_s
                    local.set 59
                    i64.const 2147483647
                    local.set 60
                    local.get 57
                    local.get 60
                    i64.le_s
                    local.set 61
                    local.get 59
                    local.get 61
                    i32.and
                    local.set 62
                    local.get 24
                    local.get 30
                    i32.mul
                    local.set 63
                    local.get 27
                    local.get 63
                    i32.add
                    local.set 64
                    local.get 64
                    i64.extend_i32_s
                    local.set 65
                    local.get 1
                    local.get 10
                    i32.div_u
                    local.set 66
                    local.get 10
                    local.set 67
                    local.get 66
                    local.get 67
                    i32.mul
                    local.set 68
                    local.get 68
                    i64.extend_i32_s
                    local.set 69
                    local.get 65
                    local.set 70
                    i32.const 1
                    local.set 14
                    br 6 (;@2;)
                  end
                  local.get 70
                  local.get 12
                  i64.lt_s
                  local.set 71
                  local.get 71
                  if  ;; label = @8
                    i32.const 3
                    local.set 14
                    br 6 (;@2;)
                  else
                    i32.const 2
                    local.set 14
                    br 6 (;@2;)
                  end
                end
                i32.const 7
                local.set 14
                br 4 (;@2;)
              end
              local.get 70
              local.get 17
              i64.lt_s
              local.set 72
              local.get 72
              if  ;; label = @6
                i32.const 5
                local.set 14
                br 4 (;@2;)
              else
                i32.const 4
                local.set 14
                br 4 (;@2;)
              end
            end
            i32.const 6
            local.set 14
            br 2 (;@2;)
          end
          i64.const 0
          local.set 73
          local.get 70
          local.get 73
          i64.ge_s
          local.set 74
          local.get 70
          local.get 17
          i64.lt_s
          local.set 75
          local.get 74
          local.get 75
          i32.and
          local.set 76
          local.get 16
          local.get 70
          i32.wrap_i64
          i32.const 4
          i32.mul
          i32.add
          local.set 77
          local.get 77
          local.get 18
          i32.store
          i32.const 6
          local.set 14
          br 1 (;@2;)
        end
        local.get 70
        local.get 69
        i64.add
        local.set 78
        local.get 78
        local.set 70
        i32.const 1
        local.set 14
        br 0 (;@2;)
      end
    end
    local.get 15)
  (export "kernel" (func 24)))
