# Run while the attached inferior is stopped, before installing any breakpoints.
shell "$HDT_NATIVE_CAPTURE_BASH" "$HDT_NATIVE_CAPTURE_GUARD" verify "$HDT_NATIVE_CAPTURE_PID" "$HDT_NATIVE_CAPTURE_IDENTITY"
if $_shell_exitcode != 0
  echo HDT_NATIVE_CAPTURE_IDENTITY_MISMATCH\n
  detach
  quit 1
end
echo HDT_NATIVE_CAPTURE_IDENTITY_VERIFIED\n
